using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Infrastructure.Services
{
    /// <summary>
    /// Calculates all technical indicators used by the strategy engine.
    ///
    /// Indicators computed:
    ///   RSI  (14)          — momentum oscillator, oversold/overbought
    ///   EMA20 / EMA50      — trend direction
    ///   MACD               — momentum (EMA12 - EMA26), signal (EMA9), histogram
    ///   ATR  (14)          — volatility, used for dynamic SL/TP sizing
    ///   Volume Spike       — current volume > 1.5x rolling average
    ///   Support/Resistance — swing low / swing high over lookback window
    ///   Trend label        — "Uptrend", "Downtrend", "Sideways"
    ///
    /// All results are persisted to the IndicatorSnapshots table for history and backtesting.
    /// </summary>
    public class IndicatorCalculationService : IIndicatorService
    {
        private readonly IMarketDataService _market;
        private readonly TradingBotDbContext _db;
        private readonly ILogger<IndicatorCalculationService> _logger;

        // ── Indicator parameters ────────────────────────────────────────────
        private const int RsiPeriod = 14;
        private const int EmaShotPeriod = 20;
        private const int EmaLongPeriod = 50;
        private const int MacdFastPeriod = 12;
        private const int MacdSlowPeriod = 26;
        private const int MacdSignalPeriod = 9;
        private const int AtrPeriod = 14;
        private const int VolumeLookback = 20;
        private const decimal VolumeSpikeMultiplier = 1.5m;
        private const int SrLookback = 20;   // candles for Support/Resistance

        // Minimum candles needed for full MACD signal line (26 + 9 - 1 = 34, pad to 50)
        private const int MinCandlesRequired = 50;

        public IndicatorCalculationService(
            IMarketDataService market,
            TradingBotDbContext db,
            ILogger<IndicatorCalculationService> logger)
        {
            _market = market;
            _db = db;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        // PUBLIC: IIndicatorService
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public async Task<IndicatorSnapshot> CalculateIndicatorsAsync(
            string symbol,
            string interval = "1h",
            int candleCount = 100)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));

            symbol = symbol.ToUpperInvariant();

            // Request enough candles for EMA50 + MACD signal; take the larger of the two
            int fetchCount = Math.Max(candleCount, MinCandlesRequired);

            _logger.LogInformation(
                "Calculating indicators for {Symbol} ({Interval}, {Count} candles)",
                symbol, interval, fetchCount);

            // ── 1. Fetch candles ────────────────────────────────────────────
            IEnumerable<Candle> rawCandles;
            try
            {
                rawCandles = await _market.GetRecentCandlesAsync(symbol, fetchCount, interval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch candles for {Symbol}", symbol);
                throw;
            }

            // Binance returns candles in ascending time order; ensure that.
            var candles = rawCandles.OrderBy(c => c.OpenTime).ToList();

            if (candles.Count < MinCandlesRequired)
            {
                _logger.LogWarning(
                    "Only {Count} candles returned for {Symbol}; minimum {Min} needed for full indicators.",
                    candles.Count, symbol, MinCandlesRequired);
            }

            // ── 2. Extract price / volume arrays ───────────────────────────
            var closes = candles.Select(c => c.Close).ToList();
            var highs = candles.Select(c => c.High).ToList();
            var lows = candles.Select(c => c.Low).ToList();
            var volumes = candles.Select(c => c.Volume).ToList();

            // ── 3. Calculate each indicator ─────────────────────────────────
            decimal rsi = CalculateRsi(closes, RsiPeriod);
            decimal ema20 = CalculateEma(closes, EmaShotPeriod);
            decimal ema50 = CalculateEma(closes, EmaLongPeriod);
            decimal macd = CalculateMacdHistogram(closes);
            decimal atr = CalculateAtr(candles, AtrPeriod);
            bool volSpike = IsVolumeSpike(volumes, VolumeLookback, VolumeSpikeMultiplier);
            string trend = DetermineTrend(ema20, ema50);
            (decimal support, decimal resistance) = CalculateSupportResistance(highs, lows, SrLookback);

            // ── 4. Build snapshot ────────────────────────────────────────────
            var snapshot = new IndicatorSnapshot
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow,
                RSI = Math.Round(rsi, 4),
                EMA20 = Math.Round(ema20, 8),
                EMA50 = Math.Round(ema50, 8),
                MACD = Math.Round(macd, 8),
                ATR = Math.Round(atr, 8),
                VolumeSpike = volSpike,
                Trend = trend,
                SupportLevel = Math.Round(support, 8),
                ResistanceLevel = Math.Round(resistance, 8),
            };

            // ── 5. Persist ────────────────────────────────────────────────────
            _db.IndicatorSnapshots!.Add(snapshot);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Indicators saved for {Symbol}: RSI={RSI:F2}, EMA20={EMA20:F2}, EMA50={EMA50:F2}, " +
                "MACD={MACD:F4}, ATR={ATR:F2}, VolumeSpike={Spike}, Trend={Trend}",
                symbol, snapshot.RSI, snapshot.EMA20, snapshot.EMA50,
                snapshot.MACD, snapshot.ATR, snapshot.VolumeSpike, snapshot.Trend);

            return snapshot;
        }

        /// <inheritdoc/>
        public async Task<IndicatorSnapshot?> GetLatestSnapshotAsync(string symbol)
        {
            symbol = symbol.ToUpperInvariant();

            return await _db.IndicatorSnapshots!
                .Where(s => s.Symbol == symbol)
                .OrderByDescending(s => s.Timestamp)
                .FirstOrDefaultAsync();
        }

        /// <inheritdoc/>
        public async Task<List<IndicatorSnapshot>> GetSnapshotHistoryAsync(string symbol, int count = 24)
        {
            symbol = symbol.ToUpperInvariant();

            return await _db.IndicatorSnapshots!
                .Where(s => s.Symbol == symbol)
                .OrderByDescending(s => s.Timestamp)
                .Take(count)
                .ToListAsync();
        }

        // ════════════════════════════════════════════════════════════════════
        // PRIVATE: Indicator Calculations
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// RSI using Wilder's Smoothed Moving Average method.
        ///
        /// Algorithm:
        ///   1. Compute price changes: delta[i] = close[i] - close[i-1]
        ///   2. Split into gains (delta > 0) and losses (|delta| when delta < 0)
        ///   3. Seed: avgGain = sum(first <period> gains) / period
        ///            avgLoss = sum(first <period> losses) / period
        ///   4. Smooth: avgGain = (prev * (period-1) + current) / period
        ///   5. RSI = 100 - 100 / (1 + avgGain / avgLoss)
        ///
        /// Returns 0 if insufficient data.
        /// </summary>
        private static decimal CalculateRsi(List<decimal> closes, int period)
        {
            // Need at least period+1 closes to get period changes
            if (closes.Count < period + 1)
                return 0m;

            // Build change array
            var changes = new decimal[closes.Count - 1];
            for (int i = 1; i < closes.Count; i++)
                changes[i - 1] = closes[i] - closes[i - 1];

            // Seed averages from first <period> changes
            decimal avgGain = 0m;
            decimal avgLoss = 0m;

            for (int i = 0; i < period; i++)
            {
                if (changes[i] > 0)
                    avgGain += changes[i];
                else
                    avgLoss += Math.Abs(changes[i]);
            }

            avgGain /= period;
            avgLoss /= period;

            // Smooth the rest
            for (int i = period; i < changes.Length; i++)
            {
                decimal gain = changes[i] > 0 ? changes[i] : 0m;
                decimal loss = changes[i] < 0 ? Math.Abs(changes[i]) : 0m;

                avgGain = (avgGain * (period - 1) + gain) / period;
                avgLoss = (avgLoss * (period - 1) + loss) / period;
            }

            if (avgLoss == 0m)
                return 100m; // No losses at all → fully overbought

            decimal rs = avgGain / avgLoss;
            decimal rsi = 100m - (100m / (1m + rs));

            return rsi;
        }

        /// <summary>
        /// Exponential Moving Average using standard multiplier = 2 / (period + 1).
        ///
        /// Seed = SMA of first <period> closes, then:
        ///   EMA[i] = close[i] * multiplier + EMA[i-1] * (1 - multiplier)
        ///
        /// Returns 0 if insufficient data.
        /// </summary>
        private static decimal CalculateEma(List<decimal> closes, int period)
        {
            if (closes.Count < period)
                return 0m;

            decimal multiplier = 2m / (period + 1m);

            // Seed with SMA of the first <period> values
            decimal ema = closes.Take(period).Average();

            // Apply exponential smoothing for the rest
            for (int i = period; i < closes.Count; i++)
                ema = closes[i] * multiplier + ema * (1m - multiplier);

            return ema;
        }

        /// <summary>
        /// MACD Histogram = MACD Line - Signal Line
        ///
        ///   MACD Line   = EMA(12) - EMA(26)
        ///   Signal Line = EMA(9) of MACD Line values
        ///   Histogram   = MACD Line - Signal Line
        ///
        /// We compute the rolling MACD line across all closes, then apply EMA9 to
        /// the MACD line series to get the signal line, and return the final histogram.
        ///
        /// Returns 0 if insufficient data (need at least 26 + 9 - 1 = 34 candles).
        /// </summary>
        private static decimal CalculateMacdHistogram(List<decimal> closes)
        {
            int required = MacdSlowPeriod + MacdSignalPeriod - 1;
            if (closes.Count < required)
                return 0m;

            // Build rolling MACD line: one value per close once we have 26+ candles
            var macdLine = new List<decimal>();

            for (int i = MacdSlowPeriod - 1; i < closes.Count; i++)
            {
                // Slice closes up to index i (inclusive)
                var slice = closes.Take(i + 1).ToList();
                decimal ema12 = CalculateEma(slice, MacdFastPeriod);
                decimal ema26 = CalculateEma(slice, MacdSlowPeriod);
                macdLine.Add(ema12 - ema26);
            }

            if (macdLine.Count < MacdSignalPeriod)
                return 0m;

            // Signal line = EMA9 of the MACD line
            decimal signalLine = CalculateEma(macdLine, MacdSignalPeriod);
            decimal currentMacd = macdLine.Last();

            return currentMacd - signalLine; // Histogram
        }

        /// <summary>
        /// Average True Range using Wilder's smoothing.
        ///
        ///   True Range[i] = max(
        ///     high[i] - low[i],
        ///     |high[i] - close[i-1]|,
        ///     |low[i]  - close[i-1]|
        ///   )
        ///
        ///   Seed ATR    = average of first <period> TRs
        ///   Smoothed    = (prev * (period-1) + current_TR) / period
        ///
        /// Returns 0 if insufficient data.
        /// </summary>
        private static decimal CalculateAtr(List<Candle> candles, int period)
        {
            if (candles.Count < period + 1)
                return 0m;

            // True Range series (starts at index 1 because we need previous close)
            var trValues = new List<decimal>();

            for (int i = 1; i < candles.Count; i++)
            {
                decimal highLow = candles[i].High - candles[i].Low;
                decimal highPrevClose = Math.Abs(candles[i].High - candles[i - 1].Close);
                decimal lowPrevClose = Math.Abs(candles[i].Low - candles[i - 1].Close);

                trValues.Add(Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose)));
            }

            if (trValues.Count < period)
                return 0m;

            // Seed ATR
            decimal atr = trValues.Take(period).Average();

            // Wilder smoothing
            for (int i = period; i < trValues.Count; i++)
                atr = (atr * (period - 1) + trValues[i]) / period;

            return atr;
        }

        /// <summary>
        /// Volume Spike detection.
        ///
        /// Compares the most recent candle volume against the rolling average of the
        /// previous <lookback> candles (excluding current). A spike is detected when
        /// current volume exceeds that average by the given multiplier.
        ///
        /// Returns false if insufficient data.
        /// </summary>
        private static bool IsVolumeSpike(
            List<decimal> volumes,
            int lookback,
            decimal multiplier)
        {
            // Need lookback + 1 (the current candle)
            if (volumes.Count < lookback + 1)
                return false;

            decimal currentVolume = volumes.Last();

            // Average of the <lookback> candles before the current one
            decimal avgVolume = volumes
                .Skip(volumes.Count - lookback - 1)
                .Take(lookback)
                .Average();

            if (avgVolume == 0m)
                return false;

            return currentVolume > avgVolume * multiplier;
        }

        /// <summary>
        /// Trend label based on EMA20 vs EMA50 relationship.
        ///
        ///   EMA20 >  EMA50 * 1.001 → "Uptrend"
        ///   EMA20 <  EMA50 * 0.999 → "Downtrend"
        ///   Otherwise              → "Sideways"
        ///
        /// The ±0.1% band avoids constant flipping when EMAs are nearly equal.
        /// Returns "Unknown" if either EMA is zero (insufficient candles).
        /// </summary>
        private static string DetermineTrend(decimal ema20, decimal ema50)
        {
            if (ema20 == 0m || ema50 == 0m)
                return "Unknown";

            if (ema20 > ema50 * 1.001m)
                return "Uptrend";

            if (ema20 < ema50 * 0.999m)
                return "Downtrend";

            return "Sideways";
        }

        /// <summary>
        /// Simple Support and Resistance levels.
        ///
        ///   Support    = lowest Low  in the last <lookback> candles
        ///   Resistance = highest High in the last <lookback> candles
        ///
        /// Returns (0, 0) if insufficient data.
        /// </summary>
        private static (decimal support, decimal resistance) CalculateSupportResistance(
            List<decimal> highs,
            List<decimal> lows,
            int lookback)
        {
            if (highs.Count < lookback || lows.Count < lookback)
                return (0m, 0m);

            var recentHighs = highs.Skip(highs.Count - lookback).ToList();
            var recentLows = lows.Skip(lows.Count - lookback).ToList();

            decimal support = recentLows.Min();
            decimal resistance = recentHighs.Max();

            return (support, resistance);
        }
    }
}
