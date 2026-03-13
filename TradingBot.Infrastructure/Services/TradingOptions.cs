namespace TradingBot.Infrastructure.Services
{
    /// <summary>
    /// Operational trading settings (scan cadence and profit targeting).
    /// Defaults preserve current behavior unless explicitly enabled.
    /// </summary>
    public class TradingOptions
    {
        public int SignalScanIntervalMinutes { get; set; } = 5;
        public string SignalScanTimeframe { get; set; } = "1h";
        public int ScanCandleCount { get; set; } = 100;

        public bool UseProfitTarget { get; set; } = false;
        public decimal ProfitTargetMinUsd { get; set; } = 20m;
        public decimal ProfitTargetMaxUsd { get; set; } = 50m;
        public decimal MinRewardRiskMultiple { get; set; } = 1.5m;
        public decimal MaxRewardRiskMultiple { get; set; } = 4.0m;

        public bool AdjustStopsToFillPrice { get; set; } = true;

        // Trade pacing controls
        public int MaxTradesPerMinute { get; set; } = 3;
        public int MinSecondsBetweenTrades { get; set; } = 20;
    }
}
