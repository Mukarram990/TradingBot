namespace TradingBot.Domain.Entities
{
    public class UserAccount : BaseEntity
    {
        public string? Username { get; set; }

        public decimal TotalBalance { get; set; }

        public decimal AvailableBalance { get; set; }

        public decimal LockedBalance { get; set; }

        public bool IsActive { get; set; }
    }
}
