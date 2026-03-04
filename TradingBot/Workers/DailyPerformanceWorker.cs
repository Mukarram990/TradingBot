using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence;

namespace TradingBot.Workers
{
    /// <summary>
    /// Automatically calculates and persists DailyPerformance records.
    ///
    /// Schedule:
    ///   - On app startup: back-fills yesterday if record is missing.
    ///   - Every night at midnight UTC: calculates the day just ended.
    ///
    /// This ensures the DailyPerformances table is always populated without
    /// requiring any manual API calls. The PerformanceController endpoints
    /// simply read from this pre-computed table.
    /// </summary>
    public class DailyPerformanceWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<DailyPerformanceWorker> _logger;

        public DailyPerformanceWorker(IServiceProvider sp, ILogger<DailyPerformanceWorker> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            _logger.LogInformation("DailyPerformanceWorker started.");

            // Back-fill yesterday on startup in case the app was down at midnight
            await TryCalculateAsync(DateTime.UtcNow.Date.AddDays(-1), ct);

            while (!ct.IsCancellationRequested)
            {
                var delay = TimeUntilNextMidnightUtc();
                _logger.LogInformation("DailyPerformanceWorker: sleeping {Min} min until midnight UTC.", (int)delay.TotalMinutes);
                await Task.Delay(delay, ct);

                if (ct.IsCancellationRequested) break;

                // Calculate for the day that just ended
                await TryCalculateAsync(DateTime.UtcNow.Date.AddDays(-1), ct);
            }

            _logger.LogInformation("DailyPerformanceWorker stopped.");
        }

        private async Task TryCalculateAsync(DateTime date, CancellationToken ct)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
                await UpsertAsync(db, date, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyPerformanceWorker: error for {Date:yyyy-MM-dd}.", date);
            }
        }

        private async Task UpsertAsync(TradingBotDbContext db, DateTime date, CancellationToken ct)
        {
            var trades = await db.Trades!
                .AsNoTracking()
                .Where(t => t.Status == TradeStatus.Closed && t.EntryTime.Date == date.Date)
                .OrderBy(t => t.EntryTime)
                .ToListAsync(ct);

            if (trades.Count == 0)
            {
                _logger.LogDebug("DailyPerformanceWorker: no closed trades on {Date:yyyy-MM-dd}.", date.Date);
                return;
            }

            int wins = trades.Count(t => t.PnL > 0);
            int losses = trades.Count(t => t.PnL <= 0);
            decimal netPnL = trades.Sum(t => t.PnL ?? 0m);
            decimal winRate = Math.Round((decimal)wins / trades.Count * 100m, 2);

            // Max intra-day drawdown (largest consecutive loss run)
            decimal maxDD = 0m, running = 0m;
            foreach (var t in trades)
            {
                if (t.PnL < 0) { running += t.PnL ?? 0m; if (running < maxDD) maxDD = running; }
                else running = 0m;
            }

            var existing = await db.DailyPerformances!
                .FirstOrDefaultAsync(d => d.Date == date.Date, ct);

            if (existing != null)
            {
                existing.TotalTrades = trades.Count;
                existing.Wins = wins;
                existing.Losses = losses;
                existing.NetPnL = Math.Round(netPnL, 4);
                existing.WinRate = winRate;
                existing.MaxDrawdown = Math.Round(Math.Abs(maxDD), 4);
                existing.UpdatedAt = DateTime.UtcNow;
                db.DailyPerformances.Update(existing);
            }
            else
            {
                db.DailyPerformances!.Add(new DailyPerformance
                {
                    Date = date.Date,
                    TotalTrades = trades.Count,
                    Wins = wins,
                    Losses = losses,
                    NetPnL = Math.Round(netPnL, 4),
                    WinRate = winRate,
                    MaxDrawdown = Math.Round(Math.Abs(maxDD), 4)
                });
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "DailyPerformanceWorker [{Date:yyyy-MM-dd}]: {Total} trades | W={W} L={L} | WR={WR}% | PnL={PnL} | MaxDD={DD}",
                date.Date, trades.Count, wins, losses, winRate,
                Math.Round(netPnL, 4), Math.Round(Math.Abs(maxDD), 4));
        }

        private static TimeSpan TimeUntilNextMidnightUtc()
        {
            var midnight = DateTime.UtcNow.Date.AddDays(1);
            return (midnight - DateTime.UtcNow).Add(TimeSpan.FromSeconds(30));
        }
    }
}