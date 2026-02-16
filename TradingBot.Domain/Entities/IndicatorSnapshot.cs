namespace TradingBot.Domain.Entities
{
    public class IndicatorSnapshot : BaseEntity
    {
        public string Symbol { get; set; }
        public DateTime Timestamp { get; set; }

        public decimal RSI { get; set; }
        public decimal EMA20 { get; set; }
        public decimal EMA50 { get; set; }
        public decimal MACD { get; set; }
        public decimal ATR { get; set; }

        public bool VolumeSpike { get; set; }

        public string Trend { get; set; }
        public decimal SupportLevel { get; set; }
        public decimal ResistanceLevel { get; set; }
    }
}
