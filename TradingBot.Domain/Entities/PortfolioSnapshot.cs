namespace TradingBot.Domain.Entities
{
    public class PortfolioSnapshot : BaseEntity
    {
        public decimal TotalBalanceUSDT { get; set; }
        public decimal TotalUnrealizedPnL { get; set; }

        public int TotalOpenPositions { get; set; }
        public decimal DailyPnL { get; set; }
    }
}
