using TradingBot.Infrastructure.Services;

namespace TradingBot.API.Workers
{
    public class TradeMonitoringWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TradeMonitoringWorker> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);  // Run every 10 seconds

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
    }
}
