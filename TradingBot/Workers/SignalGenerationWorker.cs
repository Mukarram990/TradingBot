using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.AI;
using TradingBot.Persistence;

namespace TradingBot.Workers
{
    /// <summary>
    /// Phase 2 + Phase 3 combined background worker.
    ///
    /// TICK PIPELINE (every 5 minutes):
    ///   1. Outer risk gates (CanTradeToday, CircuitBreaker)
    ///   2. ScanAllPairsAsync — fetch candles, compute indicators, save snapshots
    ///   3. For each snapshot:
    ///      a. AIEnhancedStrategyEngine.EvaluateWithAIAsync
    ///         → rule engine → regime gate → AI validation (multi-provider)
    ///      b. If approved → persist TradeSignal → OpenTradeAsync
    ///   4. Tick summary log
    ///
    /// AI failures (all providers exhausted) are handled gracefully —
    /// the trade is skipped for that symbol, other symbols continue.
    /// </summary>
    public class SignalGenerationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SignalGenerationWorker> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(30);

        public SignalGenerationWorker(
            IServiceProvider serviceProvider,
            ILogger<SignalGenerationWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "Signal Generation Worker started (AI-enhanced). First scan in {Delay}s, then every {Interval}.",
                _initialDelay.TotalSeconds, _interval);

            await Task.Delay(_initialDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunTickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SignalGenerationWorker: unhandled tick error.");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Signal Generation Worker stopped.");
        }

        private async Task RunTickAsync(CancellationToken ct)
        {
            _logger.LogInformation("SignalGenerationWorker: starting tick at {Time:HH:mm:ss}", DateTime.UtcNow);

            using var scope = _serviceProvider.CreateScope();
            var sp = scope.ServiceProvider;

            var scanner = sp.GetRequiredService<IMarketScannerService>();
            var aiEngine = sp.GetRequiredService<AIEnhancedStrategyEngine>();
            var riskMgr = sp.GetRequiredService<IRiskManagementService>();
            var tradeExec = sp.GetRequiredService<ITradeExecutionService>();
            var db = sp.GetRequiredService<TradingBotDbContext>();

            // ── Outer risk gates ─────────────────────────────────────────
            if (!riskMgr.CanTradeToday())
            {
                _logger.LogInformation("SignalGenerationWorker: max daily trades reached. Scan skipped.");
                await WriteLogAsync(db, "INFO", "SignalGenerationWorker: max daily trades reached. Scan skipped.");
                return;
            }

            if (riskMgr.IsCircuitBreakerTriggered())
            {
                _logger.LogWarning("SignalGenerationWorker: circuit breaker triggered. Scan skipped.");
                await WriteLogAsync(db, "WARN", "SignalGenerationWorker: circuit breaker triggered. Scan skipped.");
                return;
            }

            // ── Scan all pairs ───────────────────────────────────────────
            List<IndicatorSnapshot> snapshots;
            try
            {
                snapshots = await scanner.ScanAllPairsAsync("1h", 100);
                _logger.LogInformation("SignalGenerationWorker: scanned {Count} pairs.", snapshots.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SignalGenerationWorker: market scan failed.");
                return;
            }

            int signalsGenerated = 0, tradesOpened = 0;

            // ── Process each pair ────────────────────────────────────────
            foreach (var snapshot in snapshots)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var (sig, trd) = await ProcessSnapshotAsync(
                        snapshot, aiEngine, riskMgr, tradeExec, db, ct);
                    signalsGenerated += sig;
                    tradesOpened += trd;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "SignalGenerationWorker: error processing snapshot for {Symbol}.",
                        snapshot.Symbol);
                }
            }

            var summary = $"SignalGenerationWorker tick complete — " +
                          $"Snapshots={snapshots.Count}, " +
                          $"SignalsGenerated={signalsGenerated}, " +
                          $"TradesOpened={tradesOpened}";
            _logger.LogInformation(summary);
            await WriteLogAsync(db, "INFO", summary);
        }

        private async Task<(int signalsGenerated, int tradesOpened)> ProcessSnapshotAsync(
            IndicatorSnapshot snapshot,
            AIEnhancedStrategyEngine aiEngine,
            IRiskManagementService riskMgr,
            ITradeExecutionService tradeExec,
            TradingBotDbContext db,
            CancellationToken ct)
        {
            int sig = 0, trd = 0;

            // ── AI-enhanced signal evaluation ────────────────────────────
            TradeSignal? signal;
            try
            {
                signal = await aiEngine.EvaluateWithAIAsync(snapshot, ct);
            }
            catch (AiAllProvidersExhaustedException ex)
            {
                _logger.LogWarning(
                    "SignalGenerationWorker [{Symbol}]: AI exhausted — skipping. {Msg}",
                    snapshot.Symbol, ex.Message);
                return (0, 0);
            }

            if (signal == null)
                return (0, 0);

            // ── Persist TradeSignal ──────────────────────────────────────
            db.TradeSignals!.Add(signal);
            await db.SaveChangesAsync(ct);
            sig = 1;

            await WriteLogAsync(db, "INFO",
                $"SignalGenerationWorker: AI-approved BUY signal for {snapshot.Symbol} " +
                $"Confidence={signal.AIConfidence}, Entry={signal.EntryPrice:F4}");

            _logger.LogInformation(
                "SignalGenerationWorker: AI-approved BUY signal for {Symbol} — " +
                "confidence={Conf}, entry={Entry:F4}, SL={SL:F4}, TP={TP:F4}",
                snapshot.Symbol, signal.AIConfidence,
                signal.EntryPrice, signal.StopLoss, signal.TakeProfit);

            // ── Per-signal risk re-check ─────────────────────────────────
            if (!riskMgr.CanTradeToday())
            {
                _logger.LogInformation(
                    "SignalGenerationWorker: max trades reached after signal for {Symbol}. Stopping.", snapshot.Symbol);
                return (sig, trd);
            }

            if (riskMgr.IsCircuitBreakerTriggered())
            {
                _logger.LogWarning(
                    "SignalGenerationWorker: circuit breaker triggered after signal for {Symbol}. Stopping.", snapshot.Symbol);
                return (sig, trd);
            }

            // ── Execute trade ────────────────────────────────────────────
            try
            {
                var order = await tradeExec.OpenTradeAsync(signal);
                trd = 1;

                await WriteLogAsync(db, "INFO",
                    $"SignalGenerationWorker: trade opened for {snapshot.Symbol} — " +
                    $"OrderId={order.ExternalOrderId}, Price={order.ExecutedPrice:F4}");

                _logger.LogInformation(
                    "SignalGenerationWorker: trade OPENED for {Symbol} — " +
                    "orderId={OrderId}, executedPrice={Price:F4}",
                    snapshot.Symbol, order.ExternalOrderId, order.ExecutedPrice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "SignalGenerationWorker: failed to open trade for {Symbol}.", snapshot.Symbol);
                await WriteLogAsync(db, "ERROR",
                    $"SignalGenerationWorker: failed to open trade for {snapshot.Symbol}: {ex.Message}",
                    ex.StackTrace);
            }

            return (sig, trd);
        }

        private static async Task WriteLogAsync(
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
            catch { /* logging failure must never crash the worker */ }
        }
    }
}