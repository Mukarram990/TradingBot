using System.Security.Cryptography;
using System.Text;

namespace TradingBot.Infrastructure.Binance
{
    /// <summary>
    /// Handles HMAC SHA256 signing required for private Binance endpoints.
    /// </summary>
    public class BinanceSignatureService
    {
        private readonly string _apiSecret;

        public BinanceSignatureService(string apiSecret)
        {
            _apiSecret = apiSecret;
        }

        public string Sign(string queryString)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_apiSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(queryString));
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }
    }
}
