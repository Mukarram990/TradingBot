namespace TradingBot.Domain.Entities
{
    public class AIResponse : BaseEntity
    {
        public string? Symbol { get; set; }

        public string? Prompt { get; set; }

        public string? RawResponse { get; set; }

        public string? ParsedAction { get; set; }

        public int Confidence { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
