using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Domain.Models;
using TradingBot.Persistence;

namespace TradingBot.Infrastructure.AI
{
    /// <summary>
    /// AI-enhanced signal evaluation pipeline.
    ///
    /// PIPELINE (per symbol per tick):
    ///   1. StrategyEngine evaluates technical indicators → raw TradeSignal? (rule-based)
    ///   2. If signal found AND market regime is favourable:
    ///   3. MultiProviderAIService validates the signal (Gemini → Groq → OpenRouter → Cohere)
    ///   4. AI result is persisted to AIResponses table
    ///   5. If AI confidence >= MinConfidenceToTrade → return enriched TradeSignal
    ///   6. Otherwise return null (trade skipped)
    ///
    /// If ALL AI providers are rate-limited, falls back to rule-based signal only
    /// (with a confidence floor of 60, which is below the 70 threshold → trade skipped).
    /// This ensures the bot never trades on AI alone AND never blocks on AI.
    /// </summary>
    public class AIEnhancedStrategyEngine
    {
        private readonly IStrategyEngine _strategy;
        private readonly IAISignalService _ai;
        private readonly MarketRegimeDetector _regimeDetector;
        private readonly TradingBotDbContext _db;
        private readonly AiOptions _opts;
        private readonly ILogger<AIEnhancedStrategyEngine> _logger;

        // Regimes where we suppress BUY signals regardless of AI confidence
        private static readonly HashSet<string> _suppressedRegimes = new()
        {
            "Bearish", "Volatile"
        };

        public AIEnhancedStrategyEngine(
            IStrategyEngine strategy,
            IAISignalService ai,
            MarketRegimeDetector regimeDetector,
            TradingBotDbContext db,
            IOptions<AiOptions> opts,
            ILogger<AIEnhancedStrategyEngine> logger)
        {
            _strategy = strategy;
            _ai = ai;
            _regimeDetector = regimeDetector;
            _db = db;
            _opts = opts.Value;
            _logger = logger;
        }

        /// <summary>
        /// Full AI-enhanced evaluation for a symbol.
        /// Returns an enriched TradeSignal with AIConfidence set to AI's verdict,
        /// or null if rules or AI say to skip.
        /// </summary>
        public async Task<TradeSignal?> EvaluateWithAIAsync(
            IndicatorSnapshot snapshot,
            CancellationToken ct = default)
        {
            var symbol = snapshot.Symbol ?? "UNKNOWN";

            // ── Step 1: Rule-based strategy ─────────────────────────────
            var rawSignal = _strategy.EvaluateSignal(snapshot);
            if (rawSignal == null)
            {
                _logger.LogDebug("AI pipeline [{Symbol}]: rule engine returned no signal.", symbol);
                return null;
            }

            // ── Step 2: Market regime gate ───────────────────────────────
            // Detect regime using cached value first; run AI detection once per 30 min
            var regime = await _regimeDetector.GetLatestRegimeAsync(symbol);
            if (string.IsNullOrEmpty(regime) || regime == "Unknown")
                regime = await _regimeDetector.DetectAndSaveAsync(symbol, snapshot);

            if (_suppressedRegimes.Contains(regime))
            {
                _logger.LogInformation(
                    "AI pipeline [{Symbol}]: signal suppressed — market regime is {Regime}.",
                    symbol, regime);
                return null;
            }

            // ── Step 3: AI validation ────────────────────────────────────
            var tradeContext = await BuildTradeContextAsync(symbol);
            AISignalResult aiResult;

            try
            {
                aiResult = await _ai.ValidateSignalAsync(snapshot, tradeContext);
                _logger.LogInformation(
                    "AI pipeline [{Symbol}]: {Provider}/{Model} → {Action} @ {Conf}% ({Risk}). {Reason}",
                    symbol, aiResult.Provider, aiResult.ModelUsed,
                    aiResult.Action, aiResult.Confidence, aiResult.RiskLevel, aiResult.Reasoning);
            }
            catch (AiAllProvidersExhaustedException ex)
            {
                _logger.LogWarning(
                    "AI pipeline [{Symbol}]: all providers exhausted — skipping trade. {Msg}",
                    symbol, ex.Message);
                // Conservative fallback: skip trade rather than trade blindly
                return null;
            }

            // ── Step 4: Persist AI response ──────────────────────────────
            await PersistAIResponseAsync(symbol, snapshot, aiResult, ct);

            // ── Step 5: AI confidence gate ───────────────────────────────
            if (!aiResult.ShouldTrade || aiResult.Confidence < _opts.MinConfidenceToTrade)
            {
                _logger.LogInformation(
                    "AI pipeline [{Symbol}]: trade rejected — action={Action}, confidence={Conf} (min={Min}).",
                    symbol, aiResult.Action, aiResult.Confidence, _opts.MinConfidenceToTrade);
                return null;
            }

            // ── Step 6: Enrich signal with AI confidence ─────────────────
            rawSignal.AIConfidence = aiResult.Confidence;
            _logger.LogInformation(
                "AI pipeline [{Symbol}]: APPROVED — confidence={Conf}%, regime={Regime}, model={Model}.",
                symbol, aiResult.Confidence, regime, aiResult.ModelUsed);

            return rawSignal;
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private async Task<string> BuildTradeContextAsync(string symbol)
        {
            var recent = await _db.Trades!
                .AsNoTracking()
                .Where(t => t.Symbol == symbol)
                .OrderByDescending(t => t.EntryTime)
                .Take(5)
                .ToListAsync();

            if (!recent.Any())
                return "No recent trades for this symbol.";

            var lines = recent.Select(t =>
                $"{t.EntryTime:MM-dd HH:mm} {t.Status} PnL={t.PnL?.ToString("F4") ?? "open"}");
            return string.Join("; ", lines);
        }

        private async Task PersistAIResponseAsync(
            string symbol,
            IndicatorSnapshot snapshot,
            AISignalResult result,
            CancellationToken ct)
        {
            try
            {
                var prompt = $"Signal validation for {symbol}: RSI={snapshot.RSI:F2}, EMA20={snapshot.EMA20:F4}";
                _db.AIResponses!.Add(new AIResponse
                {
                    Symbol = symbol,
                    Prompt = prompt,
                    RawResponse = $"Provider={result.Provider}/{result.ModelUsed} | {result.Reasoning}",
                    ParsedAction = result.Action,
                    Confidence = result.Confidence,
                    Timestamp = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Never let logging failure crash the pipeline
                _logger.LogWarning(ex, "Failed to persist AI response for {Symbol}", symbol);
            }
        }
    }
}