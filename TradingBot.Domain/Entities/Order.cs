using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

public class Order : BaseEntity
{
    public string ExternalOrderId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;

    public decimal Quantity { get; set; }
    public decimal? ExecutedPrice { get; set; }

    public TradeStatus Status { get; set; }

    public int TradeId { get; set; }
    public Trade Trade { get; set; } = null!;
}
