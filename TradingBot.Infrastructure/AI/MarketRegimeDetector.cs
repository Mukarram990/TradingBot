using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// Detects and persists the current market regime for each scanned pair.
    ///
    /// Regime classification (stored in MarketRegimes table):
    ///   Trending  — clear directional move, EMA20 well above/below EMA50
    ///   Ranging   — price oscillating in a channel, indicators neutral
    ///   Volatile  — high ATR, wide price swings, RSI extremes
    ///   Bearish   — downtrend, EMA crossover negative, RSI weak
    ///
    /// Used by AIEnhancedStrategyEngine to filter out trades in unfavourable regimes.
    /// </summary>
    public class MarketRegimeDetector
    {
        private readonly IAISignalService _ai;
        private readonly TradingBotDbContext _db;
        private readonly ILogger<MarketRegimeDetector> _logger;

        public MarketRegimeDetector(
            IAISignalService ai,
            TradingBotDbContext db,
            ILogger<MarketRegimeDetector> logger)
        {
            _ai = ai;
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Asks AI to classify the current market regime for the given symbol,
        /// persists the result to MarketRegimes, and returns the classification string.
        /// </summary>
        public async Task<string> DetectAndSaveAsync(string symbol, IndicatorSnapshot snapshot)
        {
            try
            {
                // First try rule-based detection (free, instant)
                var ruleBasedRegime = DetectByRules(snapshot);

                // Validate or override with AI assessment (costs 1 API call)
                string aiRegime;
                try
                {
                    aiRegime = await _ai.AnalyzeMarketRegimeAsync(symbol, snapshot);
                    _logger.LogDebug("AI regime for {Symbol}: rules={Rules}, AI={AI}",
                        symbol, ruleBasedRegime, aiRegime);
                }
                catch (AiAllProvidersExhaustedException)
                {
                    // Fall back to rule-based when all AI is rate-limited
                    _logger.LogWarning("All AI providers exhausted — using rule-based regime for {Symbol}", symbol);
                    aiRegime = ruleBasedRegime;
                }

                // Parse to enum
                var trend = aiRegime switch
                {
                    "Trending" => MarketTrend.Bullish,
                    "Bearish" => MarketTrend.Bearish,
                    "Volatile" => MarketTrend.Volatile,
                    _ => MarketTrend.Sideways
                };

                // ATR-based volatility score (0.0 – 1.0)
                var volatility = snapshot.ATR > 0
                    ? Math.Min(1.0m, snapshot.ATR / snapshot.EMA20 * 100)
                    : 0m;

                // Upsert (one record per symbol per day is sufficient)
                var existing = await _db.MarketRegimes!
                    .Where(m => m.Symbol == symbol
                             && m.DetectedAt.Date == DateTime.UtcNow.Date)
                    .OrderByDescending(m => m.DetectedAt)
                    .FirstOrDefaultAsync();

                if (existing != null && (DateTime.UtcNow - existing.DetectedAt).TotalMinutes < 30)
                {
                    // Update if detected less than 30 minutes ago
                    existing.Trend = trend;
                    existing.Volatility = volatility;
                    existing.DetectedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    _db.MarketRegimes.Update(existing);
                }
                else
                {
                    _db.MarketRegimes!.Add(new MarketRegime
                    {
                        Symbol = symbol,
                        Trend = trend,
                        Volatility = volatility,
                        DetectedAt = DateTime.UtcNow
                    });
                }

                await _db.SaveChangesAsync();
                return aiRegime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarketRegimeDetector: failed for {Symbol}", symbol);
                return "Ranging"; // Safe fallback
            }
        }

        /// <summary>
        /// Pure rule-based fallback — no API calls, used when AI is unavailable.
        /// </summary>
        public static string DetectByRules(IndicatorSnapshot s)
        {
            // High volatility
            if (s.ATR > 0 && s.EMA20 > 0 && (s.ATR / s.EMA20) > 0.03m)
                return "Volatile";

            // Strong uptrend
            if (s.EMA20 > s.EMA50 && s.MACD > 0 && s.RSI is > 50 and < 70)
                return "Trending";

            // Bearish
            if (s.EMA20 < s.EMA50 && s.MACD < 0 && s.RSI < 45)
                return "Bearish";

            return "Ranging";
        }

        /// <summary>
        /// Get the most recent stored regime for a symbol (avoids extra AI call per tick).
        /// </summary>
        public async Task<string> GetLatestRegimeAsync(string symbol)
        {
            var regime = await _db.MarketRegimes!
                .AsNoTracking()
                .Where(m => m.Symbol == symbol)
                .OrderByDescending(m => m.DetectedAt)
                .FirstOrDefaultAsync();

            if (regime == null) return "Ranging";

            return regime.Trend switch
            {
                MarketTrend.Bullish => "Trending",
                MarketTrend.Bearish => "Bearish",
                MarketTrend.Volatile => "Volatile",
                _ => "Ranging"
            };
        }
    }
}