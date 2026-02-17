using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Entities
{
    /// <summary>
    /// Represents a strategy or AI generated trading signal.
    /// </summary>
    public class TradeSignal : BaseEntity
    {
        public string? Symbol { get; set; } = string.Empty;

        public TradeAction Action { get; set; }

        public decimal Quantity { get; set; }

        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal AccountBalance { get; set; }
        public int AIConfidence { get; set; }

    }
}
