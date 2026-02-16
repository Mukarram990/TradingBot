namespace TradingBot.Domain.Entities
{
    public class RiskProfile : BaseEntity
    {
        public decimal MaxRiskPerTradePercent { get; set; }
        public decimal MaxDailyLossPercent { get; set; }

        public int MaxTradesPerDay { get; set; }
        public int CircuitBreakerLossCount { get; set; }

        public bool IsEnabled { get; set; }
    }
}
