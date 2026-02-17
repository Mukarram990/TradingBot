using Microsoft.Extensions.Options;
using System.Text.Json;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance.Models;

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

        public async Task<IEnumerable<Candle>> GetRecentCandlesAsync(string symbol, int limit, string interval = "1m")
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol is required.");

            if (limit <= 0 || limit > 1000)
                throw new ArgumentException("Limit must be between 1 and 1000.");

            var response = await _httpClient.GetAsync(
                $"/api/v3/klines?symbol={symbol.ToUpper()}&interval={interval}&limit={limit}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Binance kline request failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(json);

            var candles = new List<Candle>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                candles.Add(new Candle
                {
                    Symbol = symbol.ToUpper(),
                    OpenTime = DateTimeOffset
                        .FromUnixTimeMilliseconds(element[0].GetInt64())
                        .UtcDateTime,

                    Open = decimal.Parse(element[1].GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture),

                    High = decimal.Parse(element[2].GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture),

                    Low = decimal.Parse(element[3].GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture),

                    Close = decimal.Parse(element[4].GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture),

                    Volume = decimal.Parse(element[5].GetString()!,
                        System.Globalization.CultureInfo.InvariantCulture)
                });
            }

            return candles;
        }

    }
}
