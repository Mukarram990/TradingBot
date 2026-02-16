using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Entities
{
    public class MarketRegime : BaseEntity
    {
        public string Symbol { get; set; }

        public MarketTrend Trend { get; set; }

        public decimal Volatility { get; set; }

        public DateTime DetectedAt { get; set; }
    }
}
