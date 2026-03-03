// This file lives in: TradingBot.Domain/Models/AISignalResult.cs
// The Models folder sits alongside Entities, Enums, and Interfaces in TradingBot.Domain.

using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models
{
    /// <summary>
    /// Structured result returned by any AI provider after evaluating a trade signal.
    /// NOT persisted directly — the pipeline maps it to the AIResponse entity.
    /// </summary>
    public class AISignalResult
    {
        /// <summary>BUY | HOLD | SELL</summary>
        public string Action { get; set; } = "HOLD";

        /// <summary>0–100. Only open trade when >= AiOptions.MinConfidenceToTrade (default 70).</summary>
        public int Confidence { get; set; }

        /// <summary>AI's one-line reasoning — stored in AIResponse.RawResponse.</summary>
        public string Reasoning { get; set; } = string.Empty;

        /// <summary>LOW | MEDIUM | HIGH — AI's risk assessment for this trade.</summary>
        public string RiskLevel { get; set; } = "MEDIUM";

        /// <summary>Which AI provider produced this result.</summary>
        public AiProviderType Provider { get; set; }

        /// <summary>Exact model name used (e.g. gemini-2.0-flash, llama-3.1-70b-versatile).</summary>
        public string ModelUsed { get; set; } = string.Empty;

        /// <summary>True only when Action==BUY and Confidence>=70.</summary>
        public bool ShouldTrade => Action == "BUY" && Confidence >= 70;
    }
}