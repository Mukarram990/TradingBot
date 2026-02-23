using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Infrastructure.Services
{
    public interface ITradeMonitoringService
    {
        Task MonitorAndCloseTradesAsync();
    }

    public class TradeMonitoringService : ITradeMonitoringService
    {
        private readonly TradingBotDbContext _db;
        private readonly IMarketDataService _market;
        private readonly ITradeExecutionService _executor;
        private readonly ILogger<TradeMonitoringService> _logger;

        public TradeMonitoringService(
            TradingBotDbContext db,
            IMarketDataService market,
            ITradeExecutionService executor,
            ILogger<TradeMonitoringService> logger)
        {
            _db = db;
            _market = market;
            _executor = executor;
            _logger = logger;
        }

        public async Task MonitorAndCloseTradesAsync()
        {
            try
            {
                // Get all open trades
                var openTrades = await _db.Trades
                    .Where(t => t.Status == TradeStatus.Open)
                    .ToListAsync();

                if (openTrades.Count == 0)
                    return;

                _logger.LogInformation($"Monitoring {openTrades.Count} open trades...");

                foreach (var trade in openTrades)
                {
                    try
                    {
                        // Get current price
                        var currentPrice = await _market.GetCurrentPriceAsync(trade.Symbol);

                        // Check Take Profit
                        if (currentPrice >= trade.TakeProfit)
                        {
                            _logger.LogInformation($"TP hit for {trade.Symbol} at {currentPrice}. Closing trade.");
                            
                            await _executor.CloseTradeAsync(trade.ID);
                            
                            // Log the event
                            await LogTradeEventAsync(trade.ID, "TP_HIT", currentPrice);
                            continue;
                        }

                        // Check Stop Loss
                        if (currentPrice <= trade.StopLoss)
                        {
                            _logger.LogWarning($"SL hit for {trade.Symbol} at {currentPrice}. Closing trade.");
                            
                            await _executor.CloseTradeAsync(trade.ID);
                            
                            // Log the event
                            await LogTradeEventAsync(trade.ID, "SL_HIT", currentPrice);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error monitoring trade {trade.ID}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in trade monitoring service");
            }
        }

        private async Task LogTradeEventAsync(int tradeId, string eventType, decimal triggerPrice)
        {
            // Store event in SystemLog for audit trail
            var log = new SystemLog
            {
                Level = "INFO",
                Message = $"Trade {tradeId} closed by {eventType} at price {triggerPrice}"
            };

            _db.SystemLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
