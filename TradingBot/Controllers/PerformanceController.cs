using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// Performance analytics for the admin dashboard.
    ///
    ///   GET  /api/performance/daily        — daily records with date-range filter
    ///   GET  /api/performance/summary      — all-time aggregated stats
    ///   GET  /api/performance/statistics   — breakdown by symbol, by hour-of-day
    ///   POST /api/performance/calculate    — (re)compute today's DailyPerformance record
    /// </summary>
    [ApiController]
    [Route("api/performance")]
    public class PerformanceController : ControllerBase
    {
        private readonly TradingBotDbContext _db;
        private readonly ILogger<PerformanceController> _logger;

        public PerformanceController(TradingBotDbContext db, ILogger<PerformanceController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // GET /api/performance/daily
        [HttpGet("daily")]
        public async Task<IActionResult> GetDailyPerformance(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            if (pageSize < 1 || pageSize > 90) pageSize = 30;

            var query = _db.DailyPerformances!.AsNoTracking().AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(d => d.Date >= fromDate.Value.Date);
            if (toDate.HasValue)
                query = query.Where(d => d.Date <= toDate.Value.Date);

            query = query.OrderByDescending(d => d.Date);

            var totalCount = await query.CountAsync();
            var records = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                data = records
            });
        }

        // GET /api/performance/summary?period=all|month|week|today
        [HttpGet("summary")]
        public async Task<IActionResult> GetPerformanceSummary(
            [FromQuery] string period = "all")
        {
            var query = _db.Trades!.AsNoTracking()
                .Where(t => t.Status == TradeStatus.Closed)
                .AsQueryable();

            // Apply period filter
            var cutoff = period.ToLower() switch
            {
                "today" => DateTime.UtcNow.Date,
                "week" => DateTime.UtcNow.Date.AddDays(-7),
                "month" => DateTime.UtcNow.Date.AddDays(-30),
                _ => (DateTime?)null
            };

            if (cutoff.HasValue)
                query = query.Where(t => t.EntryTime >= cutoff.Value);

            var trades = await query.ToListAsync();

            if (trades.Count == 0)
                return Ok(new
                {
                    period,
                    message = "No closed trades in this period.",
                    totalTrades = 0
                });

            var wins = trades.Count(t => t.PnL > 0);
            var losses = trades.Count(t => t.PnL <= 0);
            var netPnL = trades.Sum(t => t.PnL ?? 0m);
            var avgPnL = trades.Count > 0 ? netPnL / trades.Count : 0m;

            // Winning and losing trades only
            var winTrades = trades.Where(t => t.PnL > 0).ToList();
            var lossTrades = trades.Where(t => t.PnL <= 0).ToList();

            decimal avgWin = winTrades.Count > 0 ? winTrades.Average(t => t.PnL ?? 0m) : 0m;
            decimal avgLoss = lossTrades.Count > 0 ? lossTrades.Average(t => t.PnL ?? 0m) : 0m;

            // Profit factor = sum(wins) / abs(sum(losses))
            decimal totalWins = winTrades.Sum(t => t.PnL ?? 0m);
            decimal totalLosses = Math.Abs(lossTrades.Sum(t => t.PnL ?? 0m));
            decimal profitFactor = totalLosses > 0 ? Math.Round(totalWins / totalLosses, 2) : 0m;

            // Max drawdown: largest consecutive run of losses
            decimal maxDrawdown = 0m, runningLoss = 0m;
            foreach (var t in trades.OrderBy(t => t.EntryTime))
            {
                if (t.PnL < 0) { runningLoss += t.PnL ?? 0m; maxDrawdown = Math.Min(maxDrawdown, runningLoss); }
                else { runningLoss = 0m; }
            }

            // Consecutive win/loss streaks
            int maxConsecWins = 0, maxConsecLosses = 0, curWins = 0, curLosses = 0;
            foreach (var t in trades.OrderBy(t => t.EntryTime))
            {
                if (t.PnL > 0) { curWins++; curLosses = 0; maxConsecWins = Math.Max(maxConsecWins, curWins); }
                else { curLosses++; curWins = 0; maxConsecLosses = Math.Max(maxConsecLosses, curLosses); }
            }

            return Ok(new
            {
                period,
                totalTrades = trades.Count,
                wins,
                losses,
                winRate = Math.Round(wins / (double)trades.Count * 100, 2),
                netPnL = Math.Round(netPnL, 4),
                avgPnLPerTrade = Math.Round(avgPnL, 4),
                avgWinSize = Math.Round(avgWin, 4),
                avgLossSize = Math.Round(avgLoss, 4),
                profitFactor,
                maxDrawdown = Math.Round(maxDrawdown, 4),
                bestTrade = trades.Max(t => t.PnL),
                worstTrade = trades.Min(t => t.PnL),
                consecutiveWins = maxConsecWins,
                consecutiveLosses = maxConsecLosses,
                calculatedAt = DateTime.UtcNow
            });
        }

        // GET /api/performance/statistics  -- breakdowns for the detail page
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics()
        {
            var trades = await _db.Trades!
                .AsNoTracking()
                .Where(t => t.Status == TradeStatus.Closed)
                .OrderBy(t => t.EntryTime)
                .ToListAsync();

            // By symbol
            var bySymbol = trades
                .GroupBy(t => t.Symbol ?? "UNKNOWN")
                .OrderByDescending(g => g.Count())
                .Select(g => new
                {
                    symbol = g.Key,
                    trades = g.Count(),
                    wins = g.Count(t => t.PnL > 0),
                    losses = g.Count(t => t.PnL <= 0),
                    winRate = g.Count() > 0 ? Math.Round(g.Count(t => t.PnL > 0) / (double)g.Count() * 100, 2) : 0d,
                    netPnL = Math.Round(g.Sum(t => t.PnL ?? 0m), 4),
                    avgPnL = g.Count() > 0 ? Math.Round(g.Average(t => t.PnL ?? 0m), 4) : 0m
                });

            // By hour of day (UTC) — helps identify best trading hours
            var byHour = trades
                .GroupBy(t => t.EntryTime.Hour)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    hour = g.Key.ToString("D2") + ":00 UTC",
                    trades = g.Count(),
                    wins = g.Count(t => t.PnL > 0),
                    losses = g.Count(t => t.PnL <= 0),
                    netPnL = Math.Round(g.Sum(t => t.PnL ?? 0m), 4)
                });

            // By day of week
            var byDayOfWeek = trades
                .GroupBy(t => t.EntryTime.DayOfWeek)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    day = g.Key.ToString(),
                    trades = g.Count(),
                    wins = g.Count(t => t.PnL > 0),
                    losses = g.Count(t => t.PnL <= 0),
                    netPnL = Math.Round(g.Sum(t => t.PnL ?? 0m), 4)
                });

            return Ok(new
            {
                bySymbol,
                byHour,
                byDayOfWeek,
                calculatedAt = DateTime.UtcNow
            });
        }

        // POST /api/performance/calculate  -- upsert today's DailyPerformance record
        [HttpPost("calculate")]
        public async Task<IActionResult> CalculateDailyPerformance(
            [FromQuery] DateTime? date = null)
        {
            var targetDate = (date ?? DateTime.UtcNow).Date;

            var trades = await _db.Trades!
                .AsNoTracking()
                .Where(t => t.Status == TradeStatus.Closed
                         && t.EntryTime.Date == targetDate)
                .ToListAsync();

            if (trades.Count == 0)
                return Ok(new { message = $"No closed trades on {targetDate:yyyy-MM-dd}." });

            var wins = trades.Count(t => t.PnL > 0);
            var losses = trades.Count(t => t.PnL <= 0);
            var netPnL = trades.Sum(t => t.PnL ?? 0m);

            // Max drawdown for the day
            decimal maxDrawdown = 0m, running = 0m;
            foreach (var t in trades.OrderBy(t => t.EntryTime))
            {
                if (t.PnL < 0) { running += t.PnL ?? 0m; maxDrawdown = Math.Min(maxDrawdown, running); }
                else { running = 0m; }
            }

            // Upsert: update existing record or insert new one
            var existing = await _db.DailyPerformances!
                .FirstOrDefaultAsync(d => d.Date == targetDate);

            if (existing != null)
            {
                existing.TotalTrades = trades.Count;
                existing.Wins = wins;
                existing.Losses = losses;
                existing.NetPnL = Math.Round(netPnL, 4);
                existing.WinRate = trades.Count > 0
                    ? Math.Round((decimal)wins / trades.Count * 100, 2) : 0m;
                existing.MaxDrawdown = Math.Round(Math.Abs(maxDrawdown), 4);
                existing.UpdatedAt = DateTime.UtcNow;
                _db.DailyPerformances.Update(existing);
            }
            else
            {
                _db.DailyPerformances!.Add(new DailyPerformance
                {
                    Date = targetDate,
                    TotalTrades = trades.Count,
                    Wins = wins,
                    Losses = losses,
                    NetPnL = Math.Round(netPnL, 4),
                    WinRate = trades.Count > 0
                        ? Math.Round((decimal)wins / trades.Count * 100, 2) : 0m,
                    MaxDrawdown = Math.Round(Math.Abs(maxDrawdown), 4)
                });
            }

            await _db.SaveChangesAsync();

            return Ok(new
            {
                date = targetDate.ToString("yyyy-MM-dd"),
                totalTrades = trades.Count,
                wins,
                losses,
                winRate = trades.Count > 0 ? Math.Round((double)wins / trades.Count * 100, 2) : 0d,
                netPnL = Math.Round(netPnL, 4),
                maxDrawdown = Math.Round(Math.Abs(maxDrawdown), 4),
                action = existing != null ? "updated" : "created"
            });
        }
    }
}