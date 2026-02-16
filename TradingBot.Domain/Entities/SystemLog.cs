namespace TradingBot.Domain.Entities
{
    public class SystemLog : BaseEntity
    {
        public string Level { get; set; }
        public string Message { get; set; }
        public string? StackTrace { get; set; }
    }
}
