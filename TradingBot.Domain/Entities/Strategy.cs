namespace TradingBot.Domain.Entities
{
    public class Strategy : BaseEntity
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public string Version { get; set; }

        public bool IsActive { get; set; }

        public decimal MinConfidenceRequired { get; set; }
    }
}
