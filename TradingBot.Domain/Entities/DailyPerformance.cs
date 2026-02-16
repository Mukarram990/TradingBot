namespace TradingBot.Domain.Entities
{
    public class DailyPerformance : BaseEntity
    {
        public DateTime Date { get; set; }

        public int TotalTrades { get; set; }

        public int Wins { get; set; }
        public int Losses { get; set; }

        public decimal NetPnL { get; set; }

        public decimal MaxDrawdown { get; set; }

        public decimal WinRate { get; set; }
    }
}
