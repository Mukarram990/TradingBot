using Microsoft.Extensions.Options;
using System.Text.Json;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;

namespace TradingBot.Infrastructure.Binance
{
    public class BinanceMarketDataService : IMarketDataService
    {
        private readonly HttpClient _httpClient;

        public BinanceMarketDataService(
            HttpClient httpClient,
            IOptions<BinanceOptions> options)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri(options.Value.BaseUrl);
        }

        public async Task<decimal> GetCurrentPriceAsync(string symbol)
        {
            var response = await _httpClient.GetAsync($"/api/v3/ticker/price?symbol={symbol}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            return Convert.ToDecimal(result.GetProperty("price").GetString());
        }

        public async Task<IEnumerable<Candle>> GetRecentCandlesAsync(string symbol, int limit)
        {
            var response = await _httpClient.GetAsync(
                $"/api/v3/klines?symbol={symbol}&interval=1m&limit={limit}");

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<List<List<object>>>(json);

            var candles = new List<Candle>();

            foreach (var item in data!)
            {
                candles.Add(new Candle
                {
                    Symbol = symbol,
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(item[0])).UtcDateTime,
                    Open = Convert.ToDecimal(item[1]),
                    High = Convert.ToDecimal(item[2]),
                    Low = Convert.ToDecimal(item[3]),
                    Close = Convert.ToDecimal(item[4]),
                    Volume = Convert.ToDecimal(item[5])
                });
            }

            return candles;
        }
    }
}
