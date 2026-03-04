namespace TradingBot.Domain.Entities
{
    /// <summary>
    /// Dashboard user account for TradingBot API authentication.
    /// 
    /// ⚠️ NOTE: This is for dashboard/API access ONLY.
    /// Account balance comes LIVE from Binance API, not from this table.
    /// Use BinanceAccountService.GetAccountInfoAsync() for current balance.
    /// Use PortfolioSnapshots table for historical balance records.
    /// </summary>
    public class UserAccount : BaseEntity
    {
        /// <summary>
        /// Username for dashboard access
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Is this user allowed to access the dashboard/API?
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// SHA256 hashed API key for TradingBot API authentication.
        /// Plain key is generated via POST /api/auth/generate-key
        /// and never stored in database.
        /// </summary>
        public string? ApiKeyHash { get; set; }

        /// <summary>
        /// When the API key was last generated/rotated
        /// </summary>
        public DateTime? ApiKeyGeneratedAt { get; set; }
    }
}
