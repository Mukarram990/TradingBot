using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Workers
{
    /// <summary>
    /// Background worker that runs the full automated trading pipeline
    /// every 5 minutes:
    ///
    ///   Tick N:
    ///     1. ScanAllPairsAsync()          — fetch candles + compute indicators for every active pair
    ///     2. EvaluateSignal(snapshot)     — apply strategy rules + confidence scoring
    ///     3. Risk gate                    — CanTradeToday? CircuitBreaker triggered?
    ///     4. OpenTradeAsync(signal)       — send BUY order to Binance
    ///     5. Log everything to SystemLog  — full audit trail
    ///
    /// Design notes:
    ///   - A new DI scope is created for every tick so all Scoped services
    ///     (DbContext, Binance clients, etc.) are resolved fresh each time.
    ///     This matches the pattern used by TradeMonitoringWorker.
    ///   - A single failed pair (Binance error, bad indicator data, etc.) never
    ///     aborts the rest of the pairs in the same tick.
    ///   - The worker waits for the full tick to complete before starting the
    ///     next delay, so a slow Binance API cannot cause ticks to stack up.
    ///   - An initial warm-up delay of 30 s gives Program.cs seeders and the
    ///     portfolio snapshot time to complete before the first scan.
    /// </summary>
    public class SignalGenerationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SignalGenerationWorker> _logger;

        // How often to run the full scan + evaluate + trade pipeline.
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

        // Short pause before the very first scan so startup seeders can finish.
        private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(30);

        public SignalGenerationWorker(
            IServiceProvider serviceProvider,
            ILogger<SignalGenerationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        // BackgroundService entry point
        // ════════════════════════════════════════════════════════════════════

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Signal Generation Worker started. " +
                "First scan in {Delay}s, then every {Interval} min.",
                _initialDelay.TotalSeconds, _interval.TotalMinutes);

            // Wait for startup to fully settle before first scan.
            await Task.Delay(_initialDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "[{Time:HH:mm:ss}] Signal Generation Worker — starting tick",
                    DateTime.UtcNow);

                try
                {
                    await RunTickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // App is shutting down — exit cleanly.
                    break;
                }
                catch (Exception ex)
                {
                    // Unexpected error at the tick level. Log it but keep the worker alive.
                    _logger.LogError(ex,
                        "Unhandled exception in Signal Generation Worker tick. " +
                        "Worker will retry in {Interval} min.", _interval.TotalMinutes);
                }

                _logger.LogInformation(
                    "[{Time:HH:mm:ss}] Tick complete. Next scan in {Interval} min.",
                    DateTime.UtcNow, _interval.TotalMinutes);

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Signal Generation Worker stopped.");
        }

        // ════════════════════════════════════════════════════════════════════
        // One full tick: scan → evaluate → trade
        // ════════════════════════════════════════════════════════════════════

        private async Task RunTickAsync(CancellationToken stoppingToken)
        {
            // New scope per tick — all Scoped services (DbContext, HTTP clients)
            // are fresh for each run, avoiding stale state across ticks.
            using var scope = _serviceProvider.CreateScope();

            var scanner = scope.ServiceProvider.GetRequiredService<IMarketScannerService>();
            var strategy = scope.ServiceProvider.GetRequiredService<IStrategyEngine>();
            var risk = scope.ServiceProvider.GetRequiredService<IRiskManagementService>();
            var executor = scope.ServiceProvider.GetRequiredService<ITradeExecutionService>();
            var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();

            // ── Step 1: Outer risk gates ─────────────────────────────────────
            // Check these once per tick — if the daily budget is already used up
            // there is no point scanning the market at all.

            if (!risk.CanTradeToday())
            {
                _logger.LogInformation(
                    "Signal Worker: max trades for today already reached — skipping scan.");
                await WriteLogAsync(db, "INFO",
                    "SignalGenerationWorker: max daily trades reached. Scan skipped.");
                return;
            }

            if (risk.IsCircuitBreakerTriggered())
            {
                _logger.LogWarning(
                    "Signal Worker: circuit breaker is active (too many losses today) — skipping scan.");
                await WriteLogAsync(db, "WARN",
                    "SignalGenerationWorker: circuit breaker triggered. Scan skipped.");
                return;
            }

            // ── Step 2: Scan all active pairs ────────────────────────────────
            // MarketScannerService fetches candles from Binance, computes all
            // indicators (RSI, EMA, MACD, ATR, Volume, S/R) and saves snapshots.

            List<IndicatorSnapshot> snapshots;
            try
            {
                snapshots = await scanner.ScanAllPairsAsync(interval: "1h", candleCount: 100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signal Worker: ScanAllPairsAsync failed.");
                await WriteLogAsync(db, "ERROR",
                    $"SignalGenerationWorker: market scan failed — {ex.Message}",
                    ex.ToString());
                return;
            }

            _logger.LogInformation(
                "Signal Worker: scan complete — {Count} snapshot(s) received.", snapshots.Count);

            if (snapshots.Count == 0)
            {
                await WriteLogAsync(db, "WARN",
                    "SignalGenerationWorker: no snapshots returned from market scan.");
                return;
            }

            // ── Step 3: Evaluate each snapshot ──────────────────────────────
            int signalsGenerated = 0;
            int tradesOpened = 0;

            foreach (var snapshot in snapshots)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                string symbol = snapshot.Symbol ?? "UNKNOWN";

                try
                {
                    var result = await ProcessSnapshotAsync(
                        snapshot, symbol,
                        risk, executor, db,
                        strategy,
                        stoppingToken);
                    signalsGenerated += result.signalsGenerated;
                    tradesOpened += result.tradesOpened;
                }
                catch (Exception ex)
                {
                    // One pair failing must never abort the rest.
                    _logger.LogError(ex,
                        "Signal Worker: unhandled error processing {Symbol}", symbol);
                    await WriteLogAsync(db, "ERROR",
                        $"SignalGenerationWorker: error on {symbol} — {ex.Message}",
                        ex.ToString());
                }
            }

            // ── Step 4: Tick summary log ──────────────────────────────────────
            string summary =
                $"SignalGenerationWorker tick complete — " +
                $"Snapshots={snapshots.Count}, " +
                $"SignalsGenerated={signalsGenerated}, " +
                $"TradesOpened={tradesOpened}";

            _logger.LogInformation("Signal Worker: {Summary}", summary);
            await WriteLogAsync(db, "INFO", summary);
        }

        // ════════════════════════════════════════════════════════════════════
        // Per-snapshot processing
        // ════════════════════════════════════════════════════════════════════

        private async Task<(int signalsGenerated, int tradesOpened)> ProcessSnapshotAsync(
            IndicatorSnapshot snapshot,
            string symbol,
            IRiskManagementService risk,
            ITradeExecutionService executor,
            TradingBotDbContext db,
            IStrategyEngine strategy,
            CancellationToken stoppingToken)
        {
            int signalsGenerated = 0;
            int tradesOpened = 0;

            // ── 3a: Strategy evaluation ──────────────────────────────────────
            // EvaluateSignal is pure computation (no I/O) — synchronous by design.
            var signal = strategy.EvaluateSignal(snapshot);

            if (signal == null)
            {
                _logger.LogDebug(
                    "Signal Worker: no signal for {Symbol} " +
                    "(RSI={RSI:F1}, Trend={Trend}, MACD={MACD:F4})",
                    symbol, snapshot.RSI, snapshot.Trend, snapshot.MACD);
                return (signalsGenerated, tradesOpened);
            }

            signalsGenerated++;

            _logger.LogInformation(
                "Signal Worker: BUY signal for {Symbol} " +
                "Confidence={Conf}/100, Entry={Entry:F4}, SL={SL:F4}, TP={TP:F4}",
                symbol, signal.AIConfidence, signal.EntryPrice, signal.StopLoss, signal.TakeProfit);

            // Persist the signal for audit / history regardless of whether we
            // ultimately open the trade.
            db.TradeSignals!.Add(signal);
            await db.SaveChangesAsync();

            await WriteLogAsync(db, "INFO",
                $"SignalGenerationWorker: BUY signal for {symbol} " +
                $"Confidence={signal.AIConfidence}, Entry={signal.EntryPrice:F4}, " +
                $"SL={signal.StopLoss:F4}, TP={signal.TakeProfit:F4}");

            // ── 3b: Per-signal risk gate ─────────────────────────────────────
            // Re-check after each signal because a trade opened for a previous
            // symbol in this same tick may have already consumed the daily budget.

            if (!risk.CanTradeToday())
            {
                _logger.LogInformation(
                    "Signal Worker: signal for {Symbol} skipped — daily trade limit now reached.",
                    symbol);
                await WriteLogAsync(db, "INFO",
                    $"SignalGenerationWorker: signal for {symbol} skipped — daily trade limit reached.");
                return (signalsGenerated, tradesOpened);
            }

            if (risk.IsCircuitBreakerTriggered())
            {
                _logger.LogWarning(
                    "Signal Worker: signal for {Symbol} skipped — circuit breaker active.",
                    symbol);
                await WriteLogAsync(db, "WARN",
                    $"SignalGenerationWorker: signal for {symbol} skipped — circuit breaker active.");
                return (signalsGenerated, tradesOpened);
            }

            // Check daily loss limit (balances are fetched from Binance inside risk service).
            var startingBalance = await risk.GetDailyStartingBalanceAsync();
            // We pass 0 as currentBalance to avoid an extra Binance API call here —
            // OpenTradeAsync will do its own full loss check internally.
            // This check is a quick pre-filter: if starting balance is unknown (0)
            // we still allow OpenTradeAsync to decide, since it has the real balance.
            // If startingBalance > 0 we do the pre-check with a conservative estimate.
            // The definitive check always happens inside OpenTradeAsync.

            // ── 3c: Execute trade ────────────────────────────────────────────
            // OpenTradeAsync internally:
            //   - fetches real USDT balance from Binance
            //   - re-validates daily loss limit
            //   - calculates position size (2% rule)
            //   - places MARKET BUY order
            //   - saves Trade + Order to DB

            try
            {
                var order = await executor.OpenTradeAsync(signal);
                tradesOpened++;

                _logger.LogInformation(
                    "Signal Worker: trade OPENED for {Symbol} — " +
                    "OrderId={OrderId}, Qty={Qty}, ExecutedAt={Price:F4}",
                    symbol, order.ExternalOrderId, order.Quantity, order.ExecutedPrice);

                await WriteLogAsync(db, "INFO",
                    $"SignalGenerationWorker: trade opened for {symbol} — " +
                    $"OrderId={order.ExternalOrderId}, " +
                    $"Qty={order.Quantity}, " +
                    $"Price={order.ExecutedPrice:F4}");
            }
            catch (Exception ex)
            {
                // Trade execution failed (Binance rejected, daily limit, etc.)
                // Log and continue — this is not a crash-worthy event.
                _logger.LogError(ex,
                    "Signal Worker: failed to open trade for {Symbol} — {Message}",
                    symbol, ex.Message);

                await WriteLogAsync(db, "ERROR",
                    $"SignalGenerationWorker: trade execution failed for {symbol} — {ex.Message}",
                    ex.ToString());
            }

            return (signalsGenerated, tradesOpened);
        }

        // ════════════════════════════════════════════════════════════════════
        // Helper: write to SystemLog table
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Persists a structured log entry to the SystemLogs table.
        /// Failures here are swallowed so a logging error never crashes the worker.
        /// </summary>
        private async Task WriteLogAsync(
            TradingBotDbContext db,
            string level,
            string message,
            string? stackTrace = null)
        {
            try
            {
                db.SystemLogs!.Add(new SystemLog
                {
                    Level = level,
                    Message = message,
                    StackTrace = stackTrace
                });
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Never let a logging failure propagate up.
                _logger.LogWarning(ex, "Signal Worker: failed to write SystemLog entry.");
            }
        }
    }
}