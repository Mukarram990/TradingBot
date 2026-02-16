using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Infrastructure.Binance
{
    public class BinanceTradeExecutionService : ITradeExecutionService
    {
        private readonly HttpClient _httpClient;
        private readonly BinanceOptions _options;
        private readonly BinanceSignatureService _signatureService;
        private readonly TradingBotDbContext _dbContext;

        public BinanceTradeExecutionService(
            HttpClient httpClient,
            IOptions<BinanceOptions> options,
            TradingBotDbContext dbContext)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _dbContext = dbContext;

            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
            _signatureService = new BinanceSignatureService(_options.ApiSecret);
        }

        public async Task<Order> ExecuteOrderAsync(TradeSignal signal)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var query = new StringBuilder();
            query.Append($"symbol={signal.Symbol}");
            query.Append($"&side={(signal.Action == TradeAction.Buy ? "BUY" : "SELL")}");
            query.Append("&type=MARKET");
            query.Append($"&quantity={signal.Quantity}");
            query.Append($"&timestamp={timestamp}");

            var signature = _signatureService.Sign(query.ToString());
            query.Append($"&signature={signature}");

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/order")
            {
                Content = new StringContent(query.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded")
            };

            request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Binance order failed: {error}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            decimal? executedPrice = null;

            if (result.TryGetProperty("fills", out var fills) && fills.GetArrayLength() > 0)
            {
                executedPrice = decimal.Parse(
                    fills[0].GetProperty("price").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);
            }


            var order = new Order
            {
                ExternalOrderId = result.GetProperty("orderId").GetInt64().ToString(),
                Symbol = signal.Symbol ?? "",
                Quantity = signal.Quantity,
                ExecutedPrice = executedPrice,
                Status = TradeStatus.Open
            };

            if (_dbContext.Orders is null)
            {
                throw new InvalidOperationException("Orders DbSet is not initialized.");
            }

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            return order;
        }

        public async Task<bool> CancelOrderAsync(string orderId)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var query = $"orderId={orderId}&timestamp={timestamp}";
            var signature = _signatureService.Sign(query);
            query += $"&signature={signature}";

            var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v3/order?{query}");
            request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

            var response = await _httpClient.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
    }
}
