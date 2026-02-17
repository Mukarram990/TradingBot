using System.Text.Json.Serialization;

namespace TradingBot.Infrastructure.Binance.Models
{
    public class BinanceAccountResponse
    {
        [JsonPropertyName("balances")]
        public List<BinanceBalance> Balances { get; set; } = new();
    }

    public class BinanceBalance
    {
        [JsonPropertyName("asset")]
        public string Asset { get; set; } = string.Empty;

        [JsonPropertyName("free")]
        public string Free { get; set; } = string.Empty;

        [JsonPropertyName("locked")]
        public string Locked { get; set; } = string.Empty;
    }
}
