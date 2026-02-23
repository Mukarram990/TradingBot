using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using TradingBot.Application;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance.Models;
using TradingBot.Persistence;

namespace TradingBot.Infrastructure.Binance
{
    public class BinanceTradeExecutionService : ITradeExecutionService
    {
        private readonly HttpClient _httpClient;

        private readonly BinanceOptions _options;
        private readonly BinanceSignatureService _signatureService;
        private readonly TradingBotDbContext _dbContext;
        private readonly BinanceAccountService _accountService;
        private readonly RiskManagementService _risk;


        public BinanceTradeExecutionService(HttpClient httpClient, IOptions<BinanceOptions> options, TradingBotDbContext dbContext, RiskManagementService risk, BinanceAccountService accountService)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _dbContext = dbContext;
            _risk = risk;
            _accountService = accountService;

            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
            _signatureService = new BinanceSignatureService(_options.ApiSecret);
        }



        public async Task<Order> OpenTradeAsync(TradeSignal signal)
        {
            // 🔒 1️⃣ Max trades per day
            if (!_risk.CanTradeToday())
                throw new Exception("Max trades per day reached.");

            // 🔒 2️⃣ Circuit breaker
            if (_risk.IsCircuitBreakerTriggered())
                throw new Exception("Circuit breaker triggered. Too many losing trades.");

            // 🔒 3️⃣ Stop loss validation
            if (!_risk.IsStopLossValid(signal.EntryPrice, signal.StopLoss))
                throw new Exception("Invalid Stop Loss.");

            // 🔒 4️⃣ Position sizing (example balance for now)
            // 🔹 Get real USDT balance from Binance
            var accountBalance = await _accountService.GetAssetBalanceAsync("USDT");

            if (accountBalance <= 0)
                throw new Exception("Insufficient USDT balance.");


            var calculatedQuantity = _risk.CalculatePositionSize(
                accountBalance,
                signal.EntryPrice,
                signal.StopLoss);

            signal.Quantity = calculatedQuantity;
            if (signal.Action != TradeAction.Buy)
                throw new InvalidOperationException("Only BUY entry is supported in this method.");

            // 1️⃣ Create Trade (Position)
            var trade = new Trade
            {
                Symbol = signal.Symbol,
                Quantity = signal.Quantity,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                Status = TradeStatus.Open,
                EntryTime = DateTime.UtcNow,
                AIConfidence = signal.AIConfidence
            };

            _dbContext.Trades.Add(trade);
            await _dbContext.SaveChangesAsync(); // Needed to generate Trade.ID

            // 2️⃣ Sync time with Binance
            var serverTime = await GetServerTimeAsync();
            var localTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeOffset = serverTime - localTime;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeOffset;

            // 3️⃣ Build order query
            var query = new StringBuilder();
            query.Append($"symbol={signal.Symbol}");
            query.Append("&side=BUY");
            query.Append("&type=MARKET");
            query.Append($"&quantity={signal.Quantity}");
            query.Append($"&timestamp={timestamp}");
            query.Append("&recvWindow=5000");

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

                // rollback trade if order fails
                _dbContext.Trades.Remove(trade);
                await _dbContext.SaveChangesAsync();

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

            // 4️⃣ Update Trade EntryPrice from real execution
            if (executedPrice.HasValue)
            {
                trade.EntryPrice = executedPrice.Value;
                _dbContext.Trades.Update(trade);
            }

            // 5️⃣ Save Order linked to Trade
            var order = new Order
            {
                ExternalOrderId = result.GetProperty("orderId").GetInt64().ToString(),
                Symbol = signal.Symbol ?? "",
                Quantity = signal.Quantity,
                ExecutedPrice = executedPrice,
                Status = TradeStatus.Open,
                TradeId = trade.ID
            };

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            return order;
        }


        public async Task<Order> CloseTradeAsync(int tradeId)
        {
            // 1️⃣ Load trade
            var trade = await _dbContext.Trades
                .FirstOrDefaultAsync(t => t.ID == tradeId) ?? throw new Exception("Trade not found.");
            if (trade.Status != TradeStatus.Open)
                throw new Exception("Trade is not open.");

            // 2️⃣ Sync time with Binance
            var serverTime = await GetServerTimeAsync();
            var localTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeOffset = serverTime - localTime;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeOffset;

            // 3️⃣ Build SELL order
            var query = new StringBuilder();
            query.Append($"symbol={trade.Symbol}");
            query.Append("&side=SELL");
            query.Append("&type=MARKET");
            query.Append($"&quantity={trade.Quantity}");
            query.Append($"&timestamp={timestamp}");
            query.Append("&recvWindow=5000");

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
                throw new Exception($"Binance close order failed: {error}");
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

            if (!executedPrice.HasValue)
                throw new Exception("Could not determine executed price.");

            // 4️⃣ Calculate PnL
            var pnl = (executedPrice.Value - trade.EntryPrice) * trade.Quantity;
            var pnlPercent = ((executedPrice.Value - trade.EntryPrice) / trade.EntryPrice) * 100m;

            // 5️⃣ Update Trade
            trade.ExitPrice = executedPrice.Value;
            trade.ExitTime = DateTime.UtcNow;
            trade.PnL = Math.Round(pnl, 6);
            trade.PnLPercentage = Math.Round(pnlPercent, 4);
            trade.Status = TradeStatus.Closed;

            _dbContext.Trades.Update(trade);

            // 6️⃣ Save SELL Order linked to Trade
            var order = new Order
            {
                ExternalOrderId = result.GetProperty("orderId").GetInt64().ToString(),
                Symbol = trade.Symbol ?? "",
                Quantity = trade.Quantity,
                ExecutedPrice = executedPrice,
                Status = TradeStatus.Closed,
                TradeId = trade.ID
            };

            _dbContext.Orders.Add(order);

            await _dbContext.SaveChangesAsync();

            return order;
        }

        private async Task<long> GetServerTimeAsync()
        {
            var response = await _httpClient.GetAsync("/api/v3/time");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(json);

            return result.GetProperty("serverTime").GetInt64();
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
