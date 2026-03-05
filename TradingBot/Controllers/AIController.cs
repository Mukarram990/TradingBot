using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Infrastructure.AI;
using TradingBot.Persistence;
using TradingBot.Middleware;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// AI intelligence endpoints for the admin dashboard.
    ///
    ///   GET  /api/ai/status              — provider health + cooldown status
    ///   GET  /api/ai/responses           — paginated AIResponse history
    ///   GET  /api/ai/responses/latest    — latest AI decision per symbol
    ///   POST /api/ai/validate/{symbol}   — manually trigger AI signal validation
    ///   GET  /api/ai/regimes             — current market regime per pair
    ///   POST /api/ai/regime/{symbol}     — manually re-detect regime for a symbol
    /// </summary>
    [ApiController]
    [Route("api/ai")]
    [Authorize]
    public class AIController : ControllerBase
    {
        private readonly MultiProviderAIService _multiAI;
        private readonly AIEnhancedStrategyEngine _aiEngine;
        private readonly MarketRegimeDetector _regimeDetector;
        private readonly TradingBotDbContext _db;
        private readonly ILogger<AIController> _logger;

        public AIController(
            MultiProviderAIService multiAI,
            AIEnhancedStrategyEngine aiEngine,
            MarketRegimeDetector regimeDetector,
            TradingBotDbContext db,
            ILogger<AIController> logger)
        {
            _multiAI = multiAI;
            _aiEngine = aiEngine;
            _regimeDetector = regimeDetector;
            _db = db;
            _logger = logger;
        }

        // GET /api/ai/status  -- provider health dashboard widget
        [HttpGet("status")]
        public IActionResult GetProviderStatus()
        {
            var status = _multiAI.GetProviderStatus();
            return Ok(new
            {
                providers = status,
                checkedAt = DateTime.UtcNow,
                note = "Providers on cooldown will auto-recover after RateLimitBackoffSeconds."
            });
        }

        // GET /api/ai/responses?symbol=BTCUSDT&page=1&pageSize=20
        [HttpGet("responses")]
        public async Task<IActionResult> GetResponses(
            [FromQuery] string? symbol = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (pageSize < 1 || pageSize > 100) pageSize = 20;

            var query = _db.AIResponses!.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(symbol))
                query = query.Where(r => r.Symbol == symbol.ToUpperInvariant());

            query = query.OrderByDescending(r => r.Timestamp);

            var totalCount = await query.CountAsync();
            var data = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return Ok(new
            {
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                data
            });
        }

        // GET /api/ai/responses/latest  -- latest AI decision per symbol (for dashboard table)
        [HttpGet("responses/latest")]
        public async Task<IActionResult> GetLatestResponses()
        {
            // Latest per symbol using EF grouped query
            var all = await _db.AIResponses!
                .AsNoTracking()
                .OrderByDescending(r => r.Timestamp)
                .ToListAsync();

            var latestPerSymbol = all
                .GroupBy(r => r.Symbol)
                .Select(g => g.First())
                .OrderBy(r => r.Symbol)
                .ToList();

            return Ok(new
            {
                count = latestPerSymbol.Count,
                data = latestPerSymbol
            });
        }

        // POST /api/ai/validate/{symbol}  -- manually trigger AI validation for a symbol
        [HttpPost("validate/{symbol}")]
        public async Task<IActionResult> ValidateSignal(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest(new { error = "Symbol is required." });

            symbol = symbol.ToUpperInvariant();

            // Fetch the latest indicator snapshot for this symbol
            var snapshot = await _db.IndicatorSnapshots!
                .AsNoTracking()
                .Where(i => i.Symbol == symbol)
                .OrderByDescending(i => i.Timestamp)
                .FirstOrDefaultAsync();

            if (snapshot == null)
                return NotFound(new
                {
                    error = $"No indicator snapshot found for {symbol}.",
                    hint = $"POST /api/indicators/{symbol}/calculate first."
                });

            try
            {
                var result = await _aiEngine.EvaluateWithAIAsync(snapshot, HttpContext.RequestAborted);

                if (result == null)
                    return Ok(new
                    {
                        symbol,
                        verdict = "NO_TRADE",
                        message = "AI or strategy engine rejected the signal.",
                        snapshot = new
                        {
                            snapshot.RSI,
                            snapshot.EMA20,
                            snapshot.EMA50,
                            snapshot.MACD,
                            snapshot.Trend,
                            snapshot.Timestamp
                        }
                    });

                return Ok(new
                {
                    symbol,
                    verdict = "APPROVED",
                    aiConfidence = result.AIConfidence,
                    entryPrice = result.EntryPrice,
                    stopLoss = result.StopLoss,
                    takeProfit = result.TakeProfit,
                    quantity = result.Quantity
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Manual AI validation failed for {Symbol}", symbol);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET /api/ai/regimes  -- current regime for all active pairs
        [HttpGet("regimes")]
        public async Task<IActionResult> GetRegimes()
        {
            var regimes = await _db.MarketRegimes!
                .AsNoTracking()
                .OrderByDescending(r => r.DetectedAt)
                .ToListAsync();

            // Latest per symbol
            var latestPerSymbol = regimes
                .GroupBy(r => r.Symbol)
                .Select(g => g.First())
                .OrderBy(r => r.Symbol)
                .Select(r => new
                {
                    r.Symbol,
                    trend = r.Trend.ToString(),
                    volatility = Math.Round(r.Volatility, 4),
                    detectedAt = r.DetectedAt
                });

            return Ok(new { count = latestPerSymbol.Count(), data = latestPerSymbol });
        }

        // POST /api/ai/regime/{symbol}  -- manually re-detect regime
        [HttpPost("regime/{symbol}")]
        public async Task<IActionResult> DetectRegime(string symbol)
        {
            symbol = symbol.ToUpperInvariant();

            var snapshot = await _db.IndicatorSnapshots!
                .AsNoTracking()
                .Where(i => i.Symbol == symbol)
                .OrderByDescending(i => i.Timestamp)
                .FirstOrDefaultAsync();

            if (snapshot == null)
                return NotFound(new { error = $"No snapshot for {symbol}. Calculate indicators first." });

            try
            {
                var regime = await _regimeDetector.DetectAndSaveAsync(symbol, snapshot);
                return Ok(new { symbol, regime, detectedAt = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
