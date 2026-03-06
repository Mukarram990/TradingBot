using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces;
using TradingBot.API.Middleware;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// REST endpoints for the market scanning pipeline.
    ///
    /// Endpoints:
    ///   GET  /api/scanner/pairs                  — list all active trading pairs
    ///   POST /api/scanner/scan/{symbol}           — scan a single pair (fetch + indicators)
    ///   POST /api/scanner/scan-all                — scan every active pair
    ///   POST /api/scanner/pairs/{symbol}/activate — add or re-enable a pair
    ///   POST /api/scanner/pairs/{symbol}/deactivate — disable a pair
    /// </summary>
    [ApiController]
    [Route("api/scanner")]
    [Authorize]
    public class MarketScannerController : ControllerBase
    {
        private readonly IMarketScannerService _scanner;
        private readonly ILogger<MarketScannerController> _logger;

        public MarketScannerController(
            IMarketScannerService scanner,
            ILogger<MarketScannerController> logger)
        {
            _scanner = scanner;
            _logger = logger;
        }

        // ── GET /api/scanner/pairs ───────────────────────────────────────────

        /// <summary>
        /// Returns all trading pairs currently marked as active in the database.
        /// These are the pairs that ScanAllPairsAsync will process.
        /// </summary>
        [HttpGet("pairs")]
        public async Task<IActionResult> GetActivePairs()
        {
            var pairs = await _scanner.GetActivePairsAsync();

            return Ok(new
            {
                count = pairs.Count,
                pairs = pairs.Select(p => new
                {
                    symbol = p.Symbol,
                    baseAsset = p.BaseAsset,
                    quoteAsset = p.QuoteAsset,
                    minQty = p.MinQty,
                    stepSize = p.StepSize,
                    isActive = p.IsActive
                })
            });
        }

        // ── POST /api/scanner/scan/{symbol} ─────────────────────────────────

        /// <summary>
        /// Scans a single symbol: fetches candles from Binance, calculates all
        /// technical indicators, saves the snapshot, and returns it.
        ///
        /// Use this to get fresh indicator data on demand for any symbol.
        /// </summary>
        /// <param name="symbol">Trading pair, e.g. BTCUSDT</param>
        /// <param name="interval">Candle interval: 1m, 5m, 15m, 1h, 4h, 1d (default: 1h)</param>
        /// <param name="candleCount">Candles to use, min 50 (default: 100)</param>
        [HttpPost("scan/{symbol}")]
        public async Task<IActionResult> ScanPair(
            string symbol,
            [FromQuery] string interval = "1h",
            [FromQuery] int candleCount = 100)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            if (candleCount < 50)
                return BadRequest("candleCount must be at least 50.");

            try
            {
                var snapshot = await _scanner.ScanPairAsync(
                    symbol.ToUpperInvariant(), interval, candleCount);

                return Ok(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scan failed for {Symbol}", symbol);
                return StatusCode(500, $"Scan failed for {symbol.ToUpperInvariant()}: {ex.Message}");
            }
        }

        // ── POST /api/scanner/scan-all ───────────────────────────────────────

        /// <summary>
        /// Scans all active trading pairs in one sweep.
        /// Each pair's indicators are fetched and saved independently.
        /// Pairs that fail are skipped; the rest are still processed.
        ///
        /// Returns a summary of how many pairs succeeded/failed plus all snapshots.
        /// </summary>
        /// <param name="interval">Candle interval applied to all pairs (default: 1h)</param>
        /// <param name="candleCount">Number of candles per pair (default: 100)</param>
        [HttpPost("scan-all")]
        public async Task<IActionResult> ScanAll(
            [FromQuery] string interval = "1h",
            [FromQuery] int candleCount = 100)
        {
            if (candleCount < 50)
                return BadRequest("candleCount must be at least 50.");

            var snapshots = await _scanner.ScanAllPairsAsync(interval, candleCount);

            return Ok(new
            {
                scannedAt = DateTime.UtcNow,
                interval,
                successCount = snapshots.Count,
                snapshots
            });
        }

        // ── POST /api/scanner/pairs/{symbol}/activate ────────────────────────

        /// <summary>
        /// Adds a symbol to the active scanning list.
        /// If it already exists in the database its IsActive flag is set to true.
        /// If it does not exist a new TradingPair record is inserted.
        /// </summary>
        [HttpPost("pairs/{symbol}/activate")]
        public async Task<IActionResult> ActivatePair(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            try
            {
                var pair = await _scanner.ActivatePairAsync(symbol.ToUpperInvariant());
                return Ok(new
                {
                    message = $"{pair.Symbol} is now active and will be included in scans.",
                    pair
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ActivatePair failed for {Symbol}", symbol);
                return StatusCode(500, $"Could not activate {symbol.ToUpperInvariant()}: {ex.Message}");
            }
        }

        // ── POST /api/scanner/pairs/{symbol}/deactivate ──────────────────────

        /// <summary>
        /// Removes a symbol from the active scanning list (sets IsActive = false).
        /// The pair record stays in the database and can be re-activated later.
        /// </summary>
        [HttpPost("pairs/{symbol}/deactivate")]
        public async Task<IActionResult> DeactivatePair(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            bool success = await _scanner.DeactivatePairAsync(symbol.ToUpperInvariant());

            if (!success)
                return NotFound($"Symbol {symbol.ToUpperInvariant()} not found in TradingPairs.");

            return Ok(new
            {
                message = $"{symbol.ToUpperInvariant()} deactivated. It will no longer be scanned."
            });
        }
    }
}

