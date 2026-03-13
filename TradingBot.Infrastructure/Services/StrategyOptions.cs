namespace TradingBot.Infrastructure.Services
{
    /// <summary>
    /// Configuration for the rule-based strategy engine.
    /// Defaults match the current hardcoded behavior.
    /// </summary>
    public class StrategyOptions
    {
        /// <summary>
        /// Strategy mode: Strict | Relaxed | Momentum | Hybrid.
        /// Strict = original oversold-only logic.
        /// Relaxed = allow mild RSI + fewer confirmations.
        /// Momentum = allow trend-continuation entries.
        /// Hybrid = allow both Strict and Momentum.
        /// </summary>
        public string StrategyMode { get; set; } = "Strict";

        // Relaxed/momentum thresholds
        public decimal RelaxedRsiMax { get; set; } = 55m;
        public decimal MomentumRsiMin { get; set; } = 55m;
        public decimal MomentumRsiMax { get; set; } = 70m;
        public bool RequireVolumeSpikeForMomentum { get; set; } = true;

        public decimal RsiStrongOversold { get; set; } = 30m;
        public decimal RsiOversold { get; set; } = 45m;
        public decimal RsiOverbought { get; set; } = 70m;

        public decimal SupportProximityPct { get; set; } = 0.02m;

        public decimal SlAtrMultiplier { get; set; } = 1.5m;
        public decimal TpAtrMultiplier { get; set; } = 3.0m;

        public int MinConfidence { get; set; } = 70;

        public int PtsRsiStrongOversold { get; set; } = 30;
        public int PtsRsiMildOversold { get; set; } = 15;
        public int PtsEmaUptrend { get; set; } = 25;
        public int PtsMacdBullish { get; set; } = 20;
        public int PtsVolumeSpike { get; set; } = 15;
        public int PtsNearSupport { get; set; } = 10;
    }
}
