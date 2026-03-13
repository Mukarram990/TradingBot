using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;
using System.Text.Json;

namespace TradingBot.Infrastructure.Services
{
    /// <summary>
    /// Rule-based strategy engine that converts an IndicatorSnapshot into a
    /// TradeSignal using a weighted confidence scoring system.
    ///
    /// ── BUY REQUIREMENTS (all must pass) ────────────────────────────────────
    ///   1. RSI  < 45        (not approaching overbought territory)
    ///   2. EMA20 > EMA50    (short-term trend above long-term trend = uptrend)
    ///   3. MACD histogram > 0  (bullish momentum — buyers in control)
    ///   4. Volume spike OR price near support  (at least one confirmation)
    ///
    /// ── HARD DISQUALIFIERS (any one → null returned immediately) ────────────
    ///   • RSI  > 70         (overbought — buying here risks catching the top)
    ///   • EMA20 < EMA50     (downtrend — trend is against us)
    ///   • MACD histogram < 0  (bearish momentum — sellers in control)
    ///   • ATR  == 0         (cannot calculate SL/TP without volatility data)
    ///   • Trend == "Downtrend" or "Unknown"
    ///
    /// ── CONFIDENCE SCORING (0–100, threshold ≥ 70 to generate signal) ────────
    ///   RSI < 30  (strong oversold)          +30 pts
    ///   RSI 30–45 (mild bullish zone)        +15 pts
    ///   EMA20 > EMA50 (uptrend)              +25 pts
    ///   MACD histogram > 0                   +20 pts
    ///   Volume spike detected                +15 pts
    ///   EMA20 within 2% of SupportLevel      +10 pts   (buying near support)
    ///
    ///   Max possible: 100 pts (RSI<30 path) or 85 pts (RSI 30-45 path)
    ///   Minimum to generate signal: 70 pts
    ///
    /// ── SL/TP CALCULATION ───────────────────────────────────────────────────
    ///   Entry     = EMA20  (short-term moving average as entry estimate)
    ///   StopLoss  = entry - (ATR × 1.5)   (1.5 ATR below entry)
    ///   TakeProfit= entry + (ATR × 3.0)   (3.0 ATR above entry → 2:1 risk/reward)
    /// </summary>
    public class StrategyEngine : IStrategyEngine
    {
        private readonly IMarketScannerService _scanner;
        private readonly TradingBotDbContext _db;
        private readonly ILogger<StrategyEngine> _logger;
        private readonly StrategyOptions _opts;

        public StrategyEngine(
            IMarketScannerService scanner,
            TradingBotDbContext db,
            ILogger<StrategyEngine> logger,
            StrategyOptions options)
        {
            _scanner = scanner;
            _db = db;
            _logger = logger;
            _opts = options;
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC: IStrategyEngine
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public TradeSignal? EvaluateSignal(IndicatorSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            string symbol = snapshot.Symbol ?? "UNKNOWN";

            var customStrategies = GetActiveCustomStrategies();
            if (customStrategies.Count > 0)
            {
                var customSignal = EvaluateCustomStrategies(snapshot, customStrategies);
                if (customSignal != null)
                {
                    return customSignal;
                }
                return null;
            }

            // ── Step 1: Hard disqualifiers ────────────────────────────────
            var (isDisqualified, disqualifyReason) = CheckDisqualifiers(snapshot);
            if (isDisqualified)
            {
                _logger.LogDebug(
                    "SKIP {Symbol} — disqualified: {Reason}", symbol, disqualifyReason);
                return null;
            }

            // ── Step 2: Minimum BUY requirements ─────────────────────────
            // All three core conditions must be true to even proceed.
            bool emaUptrendConfirmed = snapshot.EMA20 > snapshot.EMA50;
            bool macdBullish = snapshot.MACD > 0;
            bool nearSupport = IsNearSupport(snapshot.EMA20, snapshot.SupportLevel);
            bool volumeSpike = snapshot.VolumeSpike;

            var mode = (_opts.StrategyMode ?? "Strict").Trim().ToLowerInvariant();

            bool strictRsi = snapshot.RSI < _opts.RsiOversold;
            bool relaxedRsi = snapshot.RSI < _opts.RelaxedRsiMax;
            bool momentumRsi = snapshot.RSI >= _opts.MomentumRsiMin && snapshot.RSI <= _opts.MomentumRsiMax;

            bool strictEntry = strictRsi && emaUptrendConfirmed && macdBullish && (volumeSpike || nearSupport);
            bool relaxedEntry = relaxedRsi && emaUptrendConfirmed && macdBullish && (volumeSpike || nearSupport);
            bool momentumEntry = momentumRsi && emaUptrendConfirmed && macdBullish && (!_opts.RequireVolumeSpikeForMomentum || volumeSpike);

            bool allowStrict = mode == "strict" || mode == "hybrid";
            bool allowRelaxed = mode == "relaxed";
            bool allowMomentum = mode == "momentum" || mode == "hybrid";

            bool passesEntry = (allowStrict && strictEntry)
                               || (allowRelaxed && relaxedEntry)
                               || (allowMomentum && momentumEntry);

            if (!passesEntry)
            {
                _logger.LogDebug(
                    "SKIP {Symbol} — entry not met (Mode={Mode}): RSI={RSI:F1}, EMA20>EMA50={EMAUp}, MACD>0={MACD}, " +
                    "VolSpike={Spike}, NearSupport={Support}",
                    symbol, _opts.StrategyMode, snapshot.RSI, emaUptrendConfirmed, macdBullish, volumeSpike, nearSupport);
                return null;
            }

            // ── Step 4: Confidence scoring ────────────────────────────────
            int score = CalculateConfidenceScore(snapshot, nearSupport);

            _logger.LogDebug(
                "SCORE {Symbol}: {Score}/100 " +
                "(RSI={RSI:F1}, EMAUp={EMAUp}, MACDBull={MACD}, Spike={Spike}, NearSupport={Near})",
                symbol, score,
                snapshot.RSI, emaUptrendConfirmed, macdBullish, volumeSpike, nearSupport);

            if (score < _opts.MinConfidence)
            {
                _logger.LogDebug(
                    "SKIP {Symbol} — confidence {Score} below threshold {Min}",
                    symbol, score, _opts.MinConfidence);
                return null;
            }

            // ── Step 5: Build the trade signal ────────────────────────────
            var signal = BuildSignal(snapshot, score);

            _logger.LogInformation(
                "BUY SIGNAL generated for {Symbol}: Confidence={Score}, " +
                "Entry={Entry:F4}, SL={SL:F4}, TP={TP:F4}, ATR={ATR:F4}",
                symbol, score, signal.EntryPrice, signal.StopLoss, signal.TakeProfit, snapshot.ATR);

            return signal;
        }

        /// <inheritdoc/>
        public async Task<TradeSignal?> EvaluateSignalForSymbolAsync(
            string symbol,
            string interval = "1h")
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));

            symbol = symbol.ToUpperInvariant();

            _logger.LogInformation(
                "Running full strategy evaluation for {Symbol} [{Interval}]",
                symbol, interval);

            // Fetch candles → calculate all indicators → save snapshot.
            // ScanPairAsync delegates to IIndicatorService which handles persistence.
            IndicatorSnapshot snapshot;
            try
            {
                snapshot = await _scanner.ScanPairAsync(symbol, interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Strategy evaluation aborted — failed to scan {Symbol}", symbol);
                throw;
            }

            // Evaluate rules against fresh snapshot.
            var signal = EvaluateSignal(snapshot);

            if (signal == null)
            {
                _logger.LogInformation(
                    "No signal for {Symbol} — conditions not met at this time.", symbol);
                return null;
            }

            // Persist the signal so we have a history of what the engine generated.
            _db.TradeSignals!.Add(signal);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Signal persisted for {Symbol} with ID {SignalId}",
                symbol, signal.ID);

            return signal;
        }

        /// <inheritdoc/>
        public async Task<List<TradeSignal>> GetRecentSignalsAsync(
            string? symbol = null,
            int count = 20)
        {
            var query = _db.TradeSignals!.AsQueryable();

            if (!string.IsNullOrWhiteSpace(symbol))
                query = query.Where(s => s.Symbol == symbol.ToUpperInvariant());

            return await query
                .OrderByDescending(s => s.CreatedAt)
                .Take(count)
                .AsNoTracking()
                .ToListAsync();
        }

        // ════════════════════════════════════════════════════════════════════
        // PRIVATE: Helpers
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks hard disqualifier conditions that immediately rule out a trade,
        /// regardless of any other indicator values.
        ///
        /// Returns (true, reason) if disqualified, (false, "") if clean.
        /// </summary>
        private (bool isDisqualified, string reason) CheckDisqualifiers(
            IndicatorSnapshot s)
        {
            // ATR must be non-zero — without it we cannot size SL/TP.
            if (s.ATR == 0m)
                return (true, "ATR is zero — cannot calculate SL/TP");

            // EMA values must be populated (service returns 0 for insufficient data).
            if (s.EMA20 == 0m || s.EMA50 == 0m)
                return (true, "EMA values not available — insufficient candle history");

            // RSI > 70: price is overbought, buying here risks catching the top.
            if (s.RSI > _opts.RsiOverbought)
                return (true, $"RSI {s.RSI:F1} is overbought (>{_opts.RsiOverbought})");

            // EMA20 < EMA50: the short-term average is below the long-term average,
            // indicating a downtrend.  We only trade with the trend.
            if (s.EMA20 < s.EMA50)
                return (true, $"EMA20 ({s.EMA20:F2}) < EMA50 ({s.EMA50:F2}) — downtrend");

            // MACD histogram < 0: bearish momentum; sellers are in control.
            if (s.MACD < 0m)
                return (true, $"MACD histogram ({s.MACD:F4}) is negative — bearish momentum");

            // Trend label sanity check (computed by IndicatorCalculationService).
            if (s.Trend == "Downtrend" || s.Trend == "Unknown")
                return (true, $"Trend label is '{s.Trend}'");

            return (false, string.Empty);
        }

        /// <summary>
        /// Calculates the confidence score (0–100) based on how many positive
        /// conditions are present and how strong each signal is.
        ///
        /// Only called after all hard disqualifiers have been checked, so we know
        /// the minimum conditions (RSI in zone, EMA uptrend, MACD > 0) are met.
        /// </summary>
        private int CalculateConfidenceScore(
            IndicatorSnapshot s,
            bool nearSupport)
        {
            int score = 0;

            // RSI condition: stronger signal if deeply oversold.
            if (s.RSI < _opts.RsiStrongOversold)
                score += _opts.PtsRsiStrongOversold;     // 30 pts — strong oversold
            else
                score += _opts.PtsRsiMildOversold;       // 15 pts — mild bullish zone

            // EMA uptrend (guaranteed true at this point, but score it anyway).
            if (s.EMA20 > s.EMA50)
                score += _opts.PtsEmaUptrend;            // 25 pts

            // MACD bullish momentum.
            if (s.MACD > 0m)
                score += _opts.PtsMacdBullish;           // 20 pts

            // Volume spike: confirms institutional interest.
            if (s.VolumeSpike)
                score += _opts.PtsVolumeSpike;           // 15 pts

            // Buying near support: reduces downside risk.
            if (nearSupport)
                score += _opts.PtsNearSupport;           // 10 pts

            return score;
        }

        /// <summary>
        /// Returns true when the given price is within SupportProximityPct (2%)
        /// above the support level — i.e., the price is "resting on support".
        ///
        ///   price ≤ support × (1 + 0.02)   → within 2% above support
        ///
        /// Returns false if support is zero (not calculated / insufficient data).
        /// </summary>
        private bool IsNearSupport(decimal price, decimal supportLevel)
        {
            if (supportLevel <= 0m || price <= 0m)
                return false;

            decimal upperBound = supportLevel * (1m + _opts.SupportProximityPct);
            return price <= upperBound;
        }

        /// <summary>
        /// Builds a TradeSignal entity from the given snapshot and confidence score.
        ///
        /// Entry    = EMA20  (short-term average = best current price estimate)
        /// StopLoss = entry − (ATR × 1.5)
        /// TakeProfit = entry + (ATR × 3.0)   → exactly 2:1 risk/reward ratio
        ///
        /// Quantity and AccountBalance are left at 0 — they will be calculated by
        /// RiskManagementService.CalculatePositionSize() when the trade is opened.
        /// </summary>
        private TradeSignal BuildSignal(IndicatorSnapshot snapshot, int confidence)
        {
            decimal entry = snapshot.EMA20;           // short-term price anchor
            decimal stopLoss = entry - (snapshot.ATR * _opts.SlAtrMultiplier);
            decimal takeProfit = entry + (snapshot.ATR * _opts.TpAtrMultiplier);

            // Safety: SL must always be strictly below entry.
            if (stopLoss >= entry)
                stopLoss = entry * 0.99m;   // fallback: 1% below entry

            // Safety: TP must always be strictly above entry.
            if (takeProfit <= entry)
                takeProfit = entry * 1.02m; // fallback: 2% above entry

            return new TradeSignal
            {
                Symbol = snapshot.Symbol,
                Action = TradeAction.Buy,
                EntryPrice = Math.Round(entry, 8),
                StopLoss = Math.Round(stopLoss, 8),
                TakeProfit = Math.Round(takeProfit, 8),
                Quantity = 0m,    // filled by RiskManagementService at execution
                AccountBalance = 0m,   // filled at execution
                AIConfidence = confidence,
            };
        }

        private List<Strategy> GetActiveCustomStrategies()
        {
            return _db.Strategies!
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
        }

        private TradeSignal? EvaluateCustomStrategies(IndicatorSnapshot snapshot, List<Strategy> strategies)
        {
            var prev = _db.IndicatorSnapshots!
                .AsNoTracking()
                .Where(s => s.Symbol == snapshot.Symbol)
                .OrderByDescending(s => s.Timestamp)
                .Skip(1)
                .FirstOrDefault();

            TradeSignal? bestSignal = null;
            decimal bestScore = 0m;

            foreach (var strategy in strategies)
            {
                var (signal, score) = EvaluateCustomStrategy(snapshot, prev, strategy);
                if (signal == null) continue;

                var weight = GetStrategyWeight(strategy);
                var finalScore = score * weight;

                if (finalScore > bestScore)
                {
                    bestScore = finalScore;
                    bestSignal = signal;
                }
            }

            if (bestSignal != null)
            {
                _logger.LogInformation(
                    "CUSTOM SIGNAL selected for {Symbol}: weightedScore={Score:F2}",
                    snapshot.Symbol, bestScore);
            }

            return bestSignal;
        }

        private (TradeSignal? signal, decimal score) EvaluateCustomStrategy(
            IndicatorSnapshot snapshot,
            IndicatorSnapshot? prev,
            Strategy strategy)
        {
            if (string.IsNullOrWhiteSpace(strategy.Description))
            {
                _logger.LogWarning("Active strategy {Id} has no definition.", strategy.ID);
                return (null, 0m);
            }

            StrategyDefinition? def;
            try
            {
                def = JsonSerializer.Deserialize<StrategyDefinition>(strategy.Description);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Active strategy {Id} has invalid JSON definition.", strategy.ID);
                return (null, 0m);
            }

            if (def == null || def.Type != "ema_crossover")
            {
                _logger.LogWarning("Active strategy {Id} has unsupported type.", strategy.ID);
                return (null, 0m);
            }

            if (def.FastEma != 20 || def.SlowEma != 50)
            {
                _logger.LogWarning(
                    "Active strategy {Id} uses EMA {Fast}/{Slow}, but only EMA20/EMA50 are available.",
                    strategy.ID, def.FastEma, def.SlowEma);
                return (null, 0m);
            }

            if (prev == null)
                return (null, 0m);

            var crossedUp = prev.EMA20 <= prev.EMA50 && snapshot.EMA20 > snapshot.EMA50;
            if (!crossedUp)
                return (null, 0m);

            if (def.RequireVolumeSpike && !snapshot.VolumeSpike)
                return (null, 0m);

            if (def.UseRsi)
            {
                if (snapshot.RSI < def.RsiMin || snapshot.RSI > def.RsiMax)
                    return (null, 0m);
            }

            if (def.UseMacd && snapshot.MACD < def.MacdMin)
                return (null, 0m);

            if (def.UseAtr && snapshot.ATR < def.AtrMin)
                return (null, 0m);

            var confidence = CalculateCustomConfidence(snapshot, def, strategy);
            if (confidence < def.MinConfidence)
                return (null, 0m);

            var signal = BuildSignal(snapshot, confidence);
            _logger.LogInformation(
                "CUSTOM SIGNAL generated for {Symbol} via {StrategyName} (ID {Id}) Conf={Conf}",
                snapshot.Symbol, strategy.Name, strategy.ID, confidence);
            return (signal, confidence);
        }

        private int CalculateCustomConfidence(IndicatorSnapshot snapshot, StrategyDefinition def, Strategy strategy)
        {
            var score = 60;
            if (def.UseRsi) score += 10;
            if (def.UseMacd && snapshot.MACD >= def.MacdMin) score += 10;
            if (def.UseAtr && snapshot.ATR >= def.AtrMin) score += 5;
            if (def.RequireVolumeSpike && snapshot.VolumeSpike) score += 10;

            var minReq = strategy.MinConfidenceRequired > 0 ? (int)strategy.MinConfidenceRequired : def.MinConfidence;
            return Math.Max(score, minReq);
        }

        private decimal GetStrategyWeight(Strategy strategy)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(strategy.Description)) return 1m;
                var def = JsonSerializer.Deserialize<StrategyDefinition>(strategy.Description);
                return def?.Weight > 0 ? def.Weight : 1m;
            }
            catch { return 1m; }
        }
    }
}
