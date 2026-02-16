namespace TradingBot.Domain.Entities
{
    public class Position : BaseEntity
    {
        public string Symbol { get; set; }

        public decimal Quantity { get; set; }
        public decimal AverageEntry { get; set; }

        public decimal UnrealizedPnL { get; set; }

        public bool IsOpen { get; set; }
    }
}
