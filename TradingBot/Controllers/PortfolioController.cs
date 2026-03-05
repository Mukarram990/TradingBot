using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance;
using TradingBot.Persistence;
using TradingBot.Middleware;
using TradingBot.Services;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/portfolio")]
    [Authorize]
    public class PortfolioController : ControllerBase
    {
        private readonly PortfolioManager _manager;
        private readonly TradingBotDbContext _db;
        private readonly BinanceAccountService _account;
        private readonly IMarketDataService _market;
        private readonly ILogger<PortfolioController> _logger;

        public PortfolioController(
            PortfolioManager manager,
            TradingBotDbContext db,
            BinanceAccountService account,
            IMarketDataService market,
            ILogger<PortfolioController> logger)
        {
            _manager = manager;
            _db = db;
            _account = account;
            _market = market;
            _logger = logger;
        }

        // POST /api/portfolio/snapshot
        [HttpPost("snapshot")]
        public async Task<IActionResult> CreateSnapshot()
        {
            try
            {
                var result = await _manager.CreateSnapshotAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create portfolio snapshot");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/portfolio/balance  -- live from Binance, never cached
        [HttpGet("balance")]
        public async Task<IActionResult> GetLiveBalance()
        {
            try
            {
                var account = await _account.GetAccountInfoAsync();

                var assets = new List<object>();
                decimal totalUsdt = 0m;

                foreach (var balance in account.Balances)
                {
                    var free = decimal.Parse(balance.Free, System.Globalization.CultureInfo.InvariantCulture);
                    var locked = decimal.Parse(balance.Locked, System.Globalization.CultureInfo.InvariantCulture);
                    var total = free + locked;
                    if (total == 0) continue;

                    decimal usdtValue = 0m;
                    if (balance.Asset == "USDT")
                    {
                        usdtValue = total;
                    }
                    else
                    {
                        try
                        {
                            var price = await _market.GetCurrentPriceAsync(balance.Asset + "USDT");
                            usdtValue = total * price;
                        }
                        catch { /* no USDT pair — skip valuation */ }
                    }

                    totalUsdt += usdtValue;
                    assets.Add(new { asset = balance.Asset, free, locked, total, usdtValue = Math.Round(usdtValue, 4) });
                }

                var today = DateTime.UtcNow.Date;
                var baseline = await _db.PortfolioSnapshots!
                    .Where(p => p.CreatedAt.Date == today)
                    .OrderBy(p => p.CreatedAt)
                    .FirstOrDefaultAsync();

                decimal todayPnL = baseline != null ? totalUsdt - baseline.TotalBalanceUSDT : 0m;

                return Ok(new
                {
                    totalUsdtValue = Math.Round(totalUsdt, 4),
                    todayPnL = Math.Round(todayPnL, 4),
                    todayPnLPercent = baseline?.TotalBalanceUSDT > 0
                        ? Math.Round(todayPnL / baseline.TotalBalanceUSDT * 100, 2) : 0m,
                    baselineBalance = baseline?.TotalBalanceUSDT,
                    assets = assets.OrderByDescending(a => ((dynamic)a).usdtValue),
                    fetchedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch live balance");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/portfolio/snapshots  -- paginated history
        [HttpGet("snapshots")]
        public async Task<IActionResult> GetSnapshots(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 30)
        {
            if (pageSize < 1 || pageSize > 200) pageSize = 30;

            var query = _db.PortfolioSnapshots!.AsNoTracking().AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(s => s.CreatedAt >= fromDate.Value.Date);
            if (toDate.HasValue)
                query = query.Where(s => s.CreatedAt < toDate.Value.Date.AddDays(1));

            query = query.OrderByDescending(s => s.CreatedAt);

            var totalCount = await query.CountAsync();
            var snapshots = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                data = snapshots
            });
        }

        // GET /api/portfolio/snapshots/today
        [HttpGet("snapshots/today")]
        public async Task<IActionResult> GetTodaysSnapshot()
        {
            var today = DateTime.UtcNow.Date;
            var snapshot = await _db.PortfolioSnapshots!
                .AsNoTracking()
                .Where(s => s.CreatedAt.Date == today)
                .OrderBy(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (snapshot == null)
                return NotFound(new { message = "No snapshot for today. POST /api/portfolio/snapshot to create one." });

            return Ok(snapshot);
        }

        // GET /api/portfolio/holdings  -- open positions with live prices
        [HttpGet("holdings")]
        public async Task<IActionResult> GetHoldings()
        {
            var openTrades = await _db.Trades!
                .AsNoTracking()
                .Where(t => t.Status == TradeStatus.Open)
                .OrderBy(t => t.Symbol)
                .ToListAsync();

            if (openTrades.Count == 0)
                return Ok(new { totalOpenPositions = 0, totalUnrealizedPnL = 0m, holdings = new List<object>() });

            var holdings = new List<object>();
            decimal totalUnrz = 0m;

            foreach (var trade in openTrades)
            {
                decimal currentPrice = 0m, unrealizedPnL = 0m, unrealizedPct = 0m;
                try
                {
                    currentPrice = await _market.GetCurrentPriceAsync(trade.Symbol!);
                    unrealizedPnL = (currentPrice - trade.EntryPrice) * trade.Quantity;
                    unrealizedPct = trade.EntryPrice > 0
                        ? Math.Round((currentPrice - trade.EntryPrice) / trade.EntryPrice * 100, 2) : 0m;
                }
                catch { /* Binance unavailable — include holding without live price */ }

                totalUnrz += unrealizedPnL;

                holdings.Add(new
                {
                    tradeId = trade.ID,
                    symbol = trade.Symbol,
                    entryPrice = trade.EntryPrice,
                    currentPrice,
                    quantity = trade.Quantity,
                    stopLoss = trade.StopLoss,
                    takeProfit = trade.TakeProfit,
                    unrealizedPnL = Math.Round(unrealizedPnL, 4),
                    unrealizedPct,
                    distanceToSLPct = trade.EntryPrice > 0
                        ? Math.Round((trade.StopLoss - currentPrice) / trade.EntryPrice * 100, 2) : 0m,
                    distanceToTPPct = trade.EntryPrice > 0
                        ? Math.Round((trade.TakeProfit - currentPrice) / trade.EntryPrice * 100, 2) : 0m,
                    aiConfidence = trade.AIConfidence,
                    entryTime = trade.EntryTime
                });
            }

            return Ok(new
            {
                totalOpenPositions = holdings.Count,
                totalUnrealizedPnL = Math.Round(totalUnrz, 4),
                holdings,
                fetchedAt = DateTime.UtcNow
            });
        }

        // GET /api/portfolio/daily-pnl?days=30  -- P&L trend for chart
        [HttpGet("daily-pnl")]
        public async Task<IActionResult> GetDailyPnL([FromQuery] int days = 30)
        {
            if (days < 1 || days > 90) days = 30;

            var from = DateTime.UtcNow.Date.AddDays(-days);
            var snapshots = await _db.PortfolioSnapshots!
                .AsNoTracking()
                .Where(s => s.CreatedAt >= from)
                .OrderBy(s => s.CreatedAt)
                .ToListAsync();

            var dailyPnL = snapshots
                .GroupBy(s => s.CreatedAt.Date)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    date = g.Key.ToString("yyyy-MM-dd"),
                    openBalance = g.First().TotalBalanceUSDT,
                    closeBalance = g.Last().TotalBalanceUSDT,
                    pnl = Math.Round(g.Last().TotalBalanceUSDT - g.First().TotalBalanceUSDT, 4),
                    pnlPercent = g.First().TotalBalanceUSDT > 0
                        ? Math.Round((g.Last().TotalBalanceUSDT - g.First().TotalBalanceUSDT)
                            / g.First().TotalBalanceUSDT * 100, 2) : 0m,
                    snapshotCount = g.Count()
                })
                .ToList();

            return Ok(new
            {
                days,
                totalDays = dailyPnL.Count,
                cumulativePnL = dailyPnL.Sum(d => d.pnl),
                data = dailyPnL
            });
        }
    }
}
