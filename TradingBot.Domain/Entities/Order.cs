using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Entities
{
    /// <summary>
    /// Represents an order placed on Binance and tracked internally.
    /// </summary>
    public class Order : BaseEntity
    {
        /// <summary>
        /// Binance exchange order ID
        /// </summary>
        public string ExternalOrderId { get; set; } = string.Empty;

        /// <summary>
        /// Trading pair (e.g. BTCUSDT)
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// Requested quantity
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>
        /// Executed price (if filled)
        /// </summary>
        public decimal? ExecutedPrice { get; set; }

        /// <summary>
        /// Current trade status
        /// </summary>
        public TradeStatus Status { get; set; }
    }
}
