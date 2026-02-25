using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// Market data endpoints.
    ///
    /// Existing:
    ///   GET /api/market/price/{symbol}    — current price
    ///   GET /api/market/candles           — recent candles
    ///
    /// New:
    ///   GET /api/market/pairs             — trading pairs from DB
    ///   GET /api/market/pairs/active      — active pairs only
    ///   GET /api/market/statistics/{symbol} — 24h stats from Binance
    ///   GET /api/market/indicators/{symbol} — latest saved indicator snapshot
    /// </summary>
    [ApiController]
    [Route("api/market")]
    public class MarketController : ControllerBase
    {
        private readonly IMarketDataService _market;
        private readonly TradingBotDbContext _db;
        private readonly ILogger<MarketController> _logger;

        public MarketController(
            IMarketDataService market,
            TradingBotDbContext db,
            ILogger<MarketController> logger)
        {
            _market = market;
            _db = db;
            _logger = logger;
        }

        // GET /api/market/price/{symbol}
        [HttpGet("price/{symbol}")]
        public async Task<IActionResult> GetPrice(string symbol)
        {
            try
            {
                var price = await _market.GetCurrentPriceAsync(symbol.ToUpperInvariant());
                return Ok(new { symbol = symbol.ToUpperInvariant(), price, fetchedAt = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET /api/market/prices?symbols=BTCUSDT,ETHUSDT,BNBUSDT  -- multi-price in one call
        [HttpGet("prices")]
        public async Task<IActionResult> GetMultiplePrices([FromQuery] string symbols)
        {
            if (string.IsNullOrWhiteSpace(symbols))
                return BadRequest(new { error = "symbols query param is required. e.g. ?symbols=BTCUSDT,ETHUSDT" });

            var list = symbols.Split(',').Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).ToList();
            var result = new List<object>();

            foreach (var sym in list)
            {
                try
                {
                    var price = await _market.GetCurrentPriceAsync(sym);
                    result.Add(new { symbol = sym, price, error = (string?)null });
                }
                catch (Exception ex)
                {
                    result.Add(new { symbol = sym, price = (decimal?)null, error = ex.Message });
                }
            }

            return Ok(new { count = result.Count, fetchedAt = DateTime.UtcNow, data = result });
        }

        // GET /api/market/candles
        [HttpGet("candles")]
        public async Task<IActionResult> GetCandles(
            [FromQuery] string symbol,
            [FromQuery] string interval = "1h",
            [FromQuery] int limit = 100)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol is required." });

            if (limit < 1 || limit > 1000) limit = 100;

            try
            {
                var candles = await _market.GetRecentCandlesAsync(symbol.ToUpperInvariant(), limit, interval);
                return Ok(candles);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // GET /api/market/pairs  -- all trading pairs stored in DB
        [HttpGet("pairs")]
        public async Task<IActionResult> GetTradingPairs(
            [FromQuery] bool? active = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (pageSize < 1 || pageSize > 200) pageSize = 50;

            var query = _db.TradingPairs!.AsNoTracking().AsQueryable();

            if (active.HasValue)
                query = query.Where(p => p.IsActive == active.Value);

            query = query.OrderBy(p => p.Symbol);

            var totalCount = await query.CountAsync();
            var pairs = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                data = pairs
            });
        }

        // GET /api/market/pairs/active  -- quick shorthand for the scanner pair list
        [HttpGet("pairs/active")]
        public async Task<IActionResult> GetActivePairs()
        {
            var pairs = await _db.TradingPairs!
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Symbol)
                .ToListAsync();

            return Ok(new { count = pairs.Count, pairs });
        }

        // GET /api/market/statistics/{symbol}  -- 24h ticker from Binance
        [HttpGet("statistics/{symbol}")]
        public async Task<IActionResult> GetMarketStatistics(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol is required." });

            symbol = symbol.ToUpperInvariant();

            try
            {
                // Current price is always available; candles give us OHLCV for 24h window
                var currentPrice = await _market.GetCurrentPriceAsync(symbol);
                var candles24h = await _market.GetRecentCandlesAsync(symbol, 24, "1h");

                var candleList = candles24h.ToList();

                decimal high24h = candleList.Count > 0 ? candleList.Max(c => c.High) : currentPrice;
                decimal low24h = candleList.Count > 0 ? candleList.Min(c => c.Low) : currentPrice;
                decimal vol24h = candleList.Count > 0 ? candleList.Sum(c => c.Volume) : 0m;
                decimal open24h = candleList.Count > 0 ? candleList.First().Open : currentPrice;
                decimal priceChg = currentPrice - open24h;
                decimal priceChgP = open24h > 0 ? Math.Round(priceChg / open24h * 100, 2) : 0m;

                return Ok(new
                {
                    symbol,
                    currentPrice,
                    open24h,
                    high24h,
                    low24h,
                    volume24h = Math.Round(vol24h, 4),
                    priceChange = Math.Round(priceChg, 4),
                    priceChangePct = priceChgP,
                    candlesUsed = candleList.Count,
                    fetchedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch market statistics for {Symbol}", symbol);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/market/indicators/{symbol}  -- latest saved IndicatorSnapshot from DB
        [HttpGet("indicators/{symbol}")]
        public async Task<IActionResult> GetLatestIndicators(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "symbol is required." });

            var snapshot = await _db.IndicatorSnapshots!
                .AsNoTracking()
                .Where(i => i.Symbol == symbol.ToUpperInvariant())
                .OrderByDescending(i => i.Timestamp)
                .FirstOrDefaultAsync();

            if (snapshot == null)
                return NotFound(new
                {
                    symbol = symbol.ToUpperInvariant(),
                    message = "No indicators calculated yet. " +
                              "POST /api/indicators/{symbol}/calculate to compute them."
                });

            return Ok(snapshot);
        }
    }
}