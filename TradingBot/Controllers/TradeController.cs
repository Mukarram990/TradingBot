using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// Full CRUD + query surface for trades.
    ///
    /// Write endpoints (existing):
    ///   POST /api/trade/open            — open a new trade via Binance
    ///   POST /api/trade/close/{id}      — close an open trade via Binance
    ///
    /// Read endpoints (new — for dashboard):
    ///   GET  /api/trades                — paginated list with optional filters
    ///   GET  /api/trades/{id}           — single trade with its linked orders
    ///   GET  /api/trades/open           — all currently open trades
    ///   GET  /api/trades/summary        — quick aggregated counts + P&amp;L
    ///   GET  /api/trades/by-symbol/{symbol} — all trades for one pair
    /// </summary>
    [ApiController]
    [Route("api")]
    public class TradeController : ControllerBase
    {
        private readonly ITradeExecutionService _trade;
        private readonly TradingBotDbContext _db;
        private readonly ILogger<TradeController> _logger;

        public TradeController(
            ITradeExecutionService trade,
            TradingBotDbContext db,
            ILogger<TradeController> logger)
        {
            _trade = trade;
            _db = db;
            _logger = logger;
        }

        // ── Write endpoints ───────────────────────────────────────────────

        [HttpPost("trade/open")]
        public async Task<IActionResult> OpenTrade([FromBody] TradeSignal signal)
        {
            try
            {
                var order = await _trade.OpenTradeAsync(signal);
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to open trade for {Symbol}", signal.Symbol);
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("trade/close/{tradeId:int}")]
        public async Task<IActionResult> CloseTrade(int tradeId)
        {
            try
            {
                var order = await _trade.CloseTradeAsync(tradeId);
                return Ok(order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close trade {TradeId}", tradeId);
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── Read endpoints ────────────────────────────────────────────────

        /// <summary>
        /// Paginated list of trades with optional filters.
        ///
        /// Query params (all optional):
        ///   status   — 1=Pending, 2=Open, 3=Closed, 4=Cancelled, 5=Failed
        ///   symbol   — e.g. BTCUSDT
        ///   fromDate — ISO 8601 start (e.g. 2025-01-01)
        ///   toDate   — ISO 8601 end
        ///   page     — 1-based page number (default 1)
        ///   pageSize — items per page (default 20, max 100)
        ///   sortBy   — entryTime | pnl | symbol (default entryTime)
        ///   desc     — true = newest first (default true)
        /// </summary>
        [HttpGet("trades")]
        public async Task<IActionResult> GetTrades(
            [FromQuery] TradeStatus? status = null,
            [FromQuery] string? symbol = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortBy = "entryTime",
            [FromQuery] bool desc = true)
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Trades!.AsNoTracking().AsQueryable();

            if (status.HasValue)
                query = query.Where(t => t.Status == status.Value);

            if (!string.IsNullOrWhiteSpace(symbol))
                query = query.Where(t => t.Symbol == symbol.ToUpperInvariant());

            if (fromDate.HasValue)
                query = query.Where(t => t.EntryTime >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(t => t.EntryTime < toDate.Value.Date.AddDays(1));

            query = (sortBy.ToLower(), desc) switch
            {
                ("pnl", true) => query.OrderByDescending(t => t.PnL),
                ("pnl", false) => query.OrderBy(t => t.PnL),
                ("symbol", true) => query.OrderByDescending(t => t.Symbol),
                ("symbol", false) => query.OrderBy(t => t.Symbol),
                (_, false) => query.OrderBy(t => t.EntryTime),
                _ => query.OrderByDescending(t => t.EntryTime)
            };

            var totalCount = await query.CountAsync();
            var trades = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                filters = new { status, symbol = symbol?.ToUpperInvariant(), fromDate, toDate },
                data = trades
            });
        }

        /// <summary>
        /// Single trade with all its linked orders.
        /// </summary>
        [HttpGet("trades/{id:int}")]
        public async Task<IActionResult> GetTrade(int id)
        {
            var trade = await _db.Trades!
                .AsNoTracking()
                .Include(t => t.Orders)
                .FirstOrDefaultAsync(t => t.ID == id);

            if (trade == null)
                return NotFound(new { error = $"Trade {id} not found." });

            return Ok(trade);
        }

        /// <summary>
        /// All currently open trades — used by the Open Positions panel.
        /// </summary>
        [HttpGet("trades/open")]
        public async Task<IActionResult> GetOpenTrades()
        {
            var trades = await _db.Trades!
                .AsNoTracking()
                .Include(t => t.Orders)
                .Where(t => t.Status == TradeStatus.Open)
                .OrderByDescending(t => t.EntryTime)
                .ToListAsync();

            return Ok(new { count = trades.Count, trades });
        }

        /// <summary>
        /// Aggregated trade summary — populates dashboard KPI cards.
        /// Optional ?symbol=BTCUSDT filter.
        /// Returns counts, win rate, net PnL, avg PnL, best/worst trade, and today's stats.
        /// </summary>
        [HttpGet("trades/summary")]
        public async Task<IActionResult> GetTradeSummary(
            [FromQuery] string? symbol = null)
        {
            var query = _db.Trades!.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(symbol))
                query = query.Where(t => t.Symbol == symbol.ToUpperInvariant());

            var all = await query.ToListAsync();
            var closed = all.Where(t => t.Status == TradeStatus.Closed).ToList();
            var open = all.Where(t => t.Status == TradeStatus.Open).ToList();
            var today = DateTime.UtcNow.Date;

            var wins = closed.Count(t => t.PnL > 0);
            var losses = closed.Count(t => t.PnL <= 0);
            var netPnL = closed.Sum(t => t.PnL ?? 0m);

            var todayTrades = closed.Where(t => t.EntryTime.Date == today).ToList();

            return Ok(new
            {
                // All-time
                totalTrades = all.Count,
                openTrades = open.Count,
                closedTrades = closed.Count,
                wins,
                losses,
                winRate = closed.Count > 0
                    ? Math.Round(wins / (double)closed.Count * 100, 2)
                    : 0d,
                netPnL = Math.Round(netPnL, 4),
                avgPnLPerTrade = closed.Count > 0
                    ? Math.Round(netPnL / closed.Count, 4)
                    : 0m,
                bestTrade = closed.Count > 0 ? (decimal?)closed.Max(t => t.PnL ?? 0m) : null,
                worstTrade = closed.Count > 0 ? (decimal?)closed.Min(t => t.PnL ?? 0m) : null,

                // Today only
                today = new
                {
                    trades = todayTrades.Count,
                    wins = todayTrades.Count(t => t.PnL > 0),
                    losses = todayTrades.Count(t => t.PnL <= 0),
                    netPnL = Math.Round(todayTrades.Sum(t => t.PnL ?? 0m), 4)
                },

                filter = symbol?.ToUpperInvariant() ?? "all",
                calculatedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// All trades for a single pair, newest first.
        /// Shorthand for GET /api/trades?symbol=X.
        /// </summary>
        [HttpGet("trades/by-symbol/{symbol}")]
        public async Task<IActionResult> GetTradesBySymbol(
            string symbol,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Symbol is required." });

            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.Trades!
                .AsNoTracking()
                .Where(t => t.Symbol == symbol.ToUpperInvariant())
                .OrderByDescending(t => t.EntryTime);

            var totalCount = await query.CountAsync();
            var trades = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new
            {
                symbol = symbol.ToUpperInvariant(),
                totalCount,
                page,
                pageSize,
                data = trades
            });
        }
    }
}