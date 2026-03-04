namespace TradingBot.Domain.Entities
{
    public class UserAccount : BaseEntity
    {
        public string? Username { get; set; }

        public decimal TotalBalance { get; set; }

        public decimal AvailableBalance { get; set; }

        public decimal LockedBalance { get; set; }

        public bool IsActive { get; set; }

        /// <summary>
        /// Hashed API key for authentication.
        /// Generate with: Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        /// </summary>
        public string? ApiKeyHash { get; set; }

        /// <summary>
        /// When the API key was last generated/rotated
        /// </summary>
        public DateTime? ApiKeyGeneratedAt { get; set; }
    }
}
