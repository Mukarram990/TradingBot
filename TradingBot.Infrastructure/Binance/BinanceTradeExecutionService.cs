using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance.Models;
using TradingBot.Persistence;
using System.Globalization;
using Microsoft.Extensions.Logging;
using TradingBot.Infrastructure.Services;

namespace TradingBot.Infrastructure.Binance
{
    public class BinanceTradeExecutionService : ITradeExecutionService
    {
        private readonly HttpClient _httpClient;

        private readonly BinanceOptions _options;
        private readonly BinanceSignatureService _signatureService;
        private readonly TradingBotDbContext _dbContext;
        private readonly BinanceAccountService _accountService;
        private readonly IRiskManagementService _risk;
        private readonly ILogger<BinanceTradeExecutionService> _logger;
        private readonly TradingOptions _tradeOpts;


        public BinanceTradeExecutionService(
            HttpClient httpClient,
            IOptions<BinanceOptions> options,
            TradingBotDbContext dbContext,
            IRiskManagementService risk,
            BinanceAccountService accountService,
            ILogger<BinanceTradeExecutionService> logger,
            IOptions<TradingOptions> tradeOptions)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _dbContext = dbContext;
            _risk = risk;
            _accountService = accountService;
            _logger = logger;
            _tradeOpts = tradeOptions.Value;

            _httpClient.BaseAddress = new Uri(_options.BaseUrl);
            _signatureService = new BinanceSignatureService(_options.ApiSecret);
        }



        public async Task<Order> OpenTradeAsync(TradeSignal signal)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            {
                _logger.LogError("Binance API credentials are missing. Configure Binance:ApiKey and Binance:ApiSecret.");
                throw new Exception("Binance API credentials are missing.");
            }

            // ?? 1?? Max trades per day
            if (!_risk.CanTradeToday())
                throw new Exception("Max trades per day reached.");

            // ?? 2?? Circuit breaker
            if (_risk.IsCircuitBreakerTriggered())
                throw new Exception("Circuit breaker triggered. Too many losing trades.");

            // ?? 3?? Stop loss validation
            if (!_risk.IsStopLossValid(signal.EntryPrice, signal.StopLoss))
                throw new Exception("Invalid Stop Loss.");

            // ?? 4?? Position sizing (example balance for now)
            // ?? Get real USDT balance from Binance
            var accountBalance = await _accountService.GetAssetBalanceAsync("USDT");

            if (accountBalance <= 0)
                throw new Exception("Insufficient USDT balance.");
            signal.AccountBalance = accountBalance;

            // ADD: Daily loss limit check
            var startingBalance = await _risk.GetDailyStartingBalanceAsync();
            if (_risk.IsDailyLossExceeded(accountBalance, startingBalance))
                throw new Exception("Daily loss limit exceeded. Trading halted.");

            var calculatedQuantity = _risk.CalculatePositionSize(
                accountBalance,
                signal.EntryPrice,
                signal.StopLoss);

            // Enforce Binance lot-size and minimum notional constraints before placing order.
            signal.Quantity = await NormalizeOrderQuantityAsync(
                signal.Symbol ?? throw new InvalidOperationException("Signal symbol is required."),
                calculatedQuantity,
                signal.EntryPrice);
            _logger.LogInformation("Normalized quantity for {Symbol}: requested={Requested}, final={Final}",
                signal.Symbol, calculatedQuantity, signal.Quantity);

            if (signal.Action != TradeAction.Buy)
                throw new InvalidOperationException("Only BUY entry is supported in this method.");

            if (_tradeOpts.UseProfitTarget)
            {
                var adjustedTp = TryApplyProfitTarget(signal);
                if (adjustedTp.HasValue)
                {
                    signal.TakeProfit = adjustedTp.Value;
                    _logger.LogInformation(
                        "Adjusted TP for {Symbol} to target ${Min}-${Max} profit (RR bounds {MinRR}-{MaxRR}). TP={TP}",
                        signal.Symbol, _tradeOpts.ProfitTargetMinUsd, _tradeOpts.ProfitTargetMaxUsd,
                        _tradeOpts.MinRewardRiskMultiple, _tradeOpts.MaxRewardRiskMultiple, signal.TakeProfit);
                }
            }

            // 1?? Create Trade (Position)
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

            // 2?? Sync time with Binance
            var serverTime = await GetServerTimeAsync();
            var localTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeOffset = serverTime - localTime;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeOffset;

            // 3?? Build order query
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

            // 4?? Update Trade EntryPrice from real execution
            if (executedPrice.HasValue)
            {
                trade.EntryPrice = executedPrice.Value;
                if (_tradeOpts.AdjustStopsToFillPrice)
                {
                    var delta = executedPrice.Value - signal.EntryPrice;
                    trade.StopLoss = Math.Round(signal.StopLoss + delta, 8);
                    trade.TakeProfit = Math.Round(signal.TakeProfit + delta, 8);

                    if (trade.StopLoss >= trade.EntryPrice)
                        trade.StopLoss = Math.Round(trade.EntryPrice * 0.99m, 8);
                    if (trade.TakeProfit <= trade.EntryPrice)
                        trade.TakeProfit = Math.Round(trade.EntryPrice * 1.02m, 8);
                }
                _dbContext.Trades.Update(trade);
            }

            // 5?? Save Order linked to Trade
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

            _logger.LogInformation(
                "Trade opened for {Symbol}. TradeId={TradeId}, OrderId={OrderId}, Qty={Qty}, Entry={Entry}",
                trade.Symbol, trade.ID, order.ExternalOrderId, trade.Quantity, trade.EntryPrice);

            return order;
        }

        private decimal? TryApplyProfitTarget(TradeSignal signal)
        {
            if (signal.Action != TradeAction.Buy)
                return null;

            var riskPerUnit = signal.EntryPrice - signal.StopLoss;
            if (riskPerUnit <= 0m || signal.Quantity <= 0m)
                return null;

            var targetUsd = (_tradeOpts.ProfitTargetMinUsd + _tradeOpts.ProfitTargetMaxUsd) / 2m;
            var maxTarget = _tradeOpts.ProfitTargetMaxUsd;
            var minTarget = _tradeOpts.ProfitTargetMinUsd;

            if (minTarget <= 0m || maxTarget <= 0m || maxTarget < minTarget)
                return null;

            // Start with midpoint target and clamp by RR bounds.
            var desiredTarget = ClampDecimal(targetUsd, minTarget, maxTarget);
            var rewardMultiple = desiredTarget / (riskPerUnit * signal.Quantity);
            rewardMultiple = ClampDecimal(rewardMultiple, _tradeOpts.MinRewardRiskMultiple, _tradeOpts.MaxRewardRiskMultiple);

            var tp = signal.EntryPrice + (riskPerUnit * rewardMultiple);
            return Math.Round(tp, 8);
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private async Task<decimal> NormalizeOrderQuantityAsync(string symbol, decimal requestedQuantity, decimal referencePrice)
        {
            var filters = await GetSymbolTradeFiltersAsync(symbol);

            if (requestedQuantity <= 0m)
                throw new Exception($"Calculated quantity is invalid for {symbol}: {requestedQuantity}.");

            if (filters.StepSize > 0m)
            {
                requestedQuantity = Math.Floor(requestedQuantity / filters.StepSize) * filters.StepSize;
            }

            requestedQuantity = Math.Round(requestedQuantity, 8, MidpointRounding.ToZero);

            if (requestedQuantity < filters.MinQty)
                throw new Exception(
                    $"Quantity {requestedQuantity} is below Binance LOT_SIZE minQty {filters.MinQty} for {symbol}.");

            var estimatedNotional = requestedQuantity * referencePrice;
            if (filters.MinNotional > 0m && estimatedNotional < filters.MinNotional)
                throw new Exception(
                    $"Order notional {estimatedNotional:F8} is below Binance MIN_NOTIONAL {filters.MinNotional} for {symbol}.");

            return requestedQuantity;
        }

        private async Task<(decimal MinQty, decimal StepSize, decimal MinNotional)> GetSymbolTradeFiltersAsync(string symbol)
        {
            var response = await _httpClient.GetAsync($"/api/v3/exchangeInfo?symbol={symbol}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<JsonElement>(json);

            if (!root.TryGetProperty("symbols", out var symbols) || symbols.GetArrayLength() == 0)
                throw new Exception($"No exchangeInfo data returned for symbol {symbol}.");

            var symbolNode = symbols[0];
            if (!symbolNode.TryGetProperty("filters", out var filters))
                throw new Exception($"No filters found in exchangeInfo for symbol {symbol}.");

            decimal minQty = 0m;
            decimal stepSize = 0m;
            decimal minNotional = 0m;

            foreach (var filter in filters.EnumerateArray())
            {
                var filterType = filter.GetProperty("filterType").GetString();
                if (filterType == "LOT_SIZE")
                {
                    minQty = decimal.Parse(filter.GetProperty("minQty").GetString() ?? "0", CultureInfo.InvariantCulture);
                    stepSize = decimal.Parse(filter.GetProperty("stepSize").GetString() ?? "0", CultureInfo.InvariantCulture);
                }
                else if (filterType == "MIN_NOTIONAL")
                {
                    minNotional = decimal.Parse(filter.GetProperty("minNotional").GetString() ?? "0", CultureInfo.InvariantCulture);
                }
            }

            if (minQty <= 0m || stepSize <= 0m)
                throw new Exception($"Invalid LOT_SIZE filters received for {symbol}.");

            return (minQty, stepSize, minNotional);
        }


        public async Task<Order> CloseTradeAsync(int tradeId)
        {
            // 1?? Load trade
            var trade = await _dbContext.Trades
                .FirstOrDefaultAsync(t => t.ID == tradeId) ?? throw new Exception("Trade not found.");
            if (trade.Status != TradeStatus.Open)
                throw new Exception("Trade is not open.");

            // 2?? Sync time with Binance
            var serverTime = await GetServerTimeAsync();
            var localTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var timeOffset = serverTime - localTime;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + timeOffset;

            // 3?? Build SELL order
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

            // 4?? Calculate PnL
            var pnl = (executedPrice.Value - trade.EntryPrice) * trade.Quantity;
            var pnlPercent = ((executedPrice.Value - trade.EntryPrice) / trade.EntryPrice) * 100m;

            // 5?? Update Trade
            trade.ExitPrice = executedPrice.Value;
            trade.ExitTime = DateTime.UtcNow;
            trade.PnL = Math.Round(pnl, 6);
            trade.PnLPercentage = Math.Round(pnlPercent, 4);
            trade.Status = TradeStatus.Closed;

            _dbContext.Trades.Update(trade);

            // 6?? Save SELL Order linked to Trade
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

            _logger.LogInformation(
                "Trade closed for {Symbol}. TradeId={TradeId}, OrderId={OrderId}, Exit={Exit}, PnL={PnL}",
                trade.Symbol, trade.ID, order.ExternalOrderId, trade.ExitPrice, trade.PnL);

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

