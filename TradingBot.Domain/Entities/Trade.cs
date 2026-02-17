using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

public class Trade : BaseEntity
{
    public string? Symbol { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }

    public decimal Quantity { get; set; }

    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }

    public decimal? PnL { get; set; }
    public decimal? PnLPercentage { get; set; }

    public TradeStatus Status { get; set; }

    public DateTime EntryTime { get; set; }
    public DateTime? ExitTime { get; set; }

    public int AIConfidence { get; set; }

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
