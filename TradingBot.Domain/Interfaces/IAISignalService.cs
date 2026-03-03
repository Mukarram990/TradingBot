using TradingBot.Domain.Entities;
using TradingBot.Domain.Models;

namespace TradingBot.Domain.Interfaces
{
    /// <summary>
    /// Contract for an AI signal validation service.
    /// Implementations wrap individual AI providers (Gemini, Groq, OpenRouter, Cohere).
    /// The multi-provider orchestrator also implements this interface.
    /// </summary>
    public interface IAISignalService
    {
        /// <summary>
        /// Ask the AI to validate a proposed BUY signal.
        /// Returns an AISignalResult indicating whether to proceed, hold or reject the trade.
        /// Throws AiAllProvidersExhaustedException when all providers are rate-limited.
        /// </summary>
        Task<AISignalResult> ValidateSignalAsync(
            IndicatorSnapshot snapshot,
            string recentTradeContext);

        /// <summary>
        /// Ask the AI for a market regime assessment (Trending/Ranging/Bearish/Volatile).
        /// Used by MarketRegimeDetector to update the MarketRegimes table.
        /// </summary>
        Task<string> AnalyzeMarketRegimeAsync(
            string symbol,
            IndicatorSnapshot snapshot);
    }
}