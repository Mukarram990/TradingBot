using TradingBot.Infrastructure.Services;
using TradingBot.Domain.Entities;
using TradingBot.Persistence;

namespace TradingBot.API.Workers
{
    public class TradeMonitoringWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TradeMonitoringWorker> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);  // Run every 10 seconds
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;

        public TradeMonitoringWorker(IServiceProvider serviceProvider, ILogger<TradeMonitoringWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Trade Monitoring Worker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var monitor = scope.ServiceProvider.GetRequiredService<ITradeMonitoringService>();
                        await monitor.MonitorAndCloseTradesAsync();
                        await WriteHeartbeatIfDueAsync(scope.ServiceProvider, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TradeMonitoringWorker");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Trade Monitoring Worker stopped");
        }

        private async Task WriteHeartbeatIfDueAsync(IServiceProvider sp, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc) < TimeSpan.FromMinutes(1))
                return;

            try
            {
                var db = sp.GetRequiredService<TradingBotDbContext>();
                db.SystemLogs!.Add(new SystemLog
                {
                    Level = "INFO",
                    Message = "TradeMonitoringWorker heartbeat"
                });
                await db.SaveChangesAsync(ct);
                _lastHeartbeatUtc = now;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TradeMonitoringWorker heartbeat write skipped.");
            }
        }
    }
}
