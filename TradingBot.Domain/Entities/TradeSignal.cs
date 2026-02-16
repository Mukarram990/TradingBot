using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Entities
{
    /// <summary>
    /// Represents a strategy or AI generated trading signal.
    /// </summary>
    public class TradeSignal : BaseEntity
    {
        public string Symbol { get; set; } = string.Empty;

        public TradeAction Action { get; set; }

        public decimal Quantity { get; set; }

        public decimal? Confidence { get; set; }
    }
}
