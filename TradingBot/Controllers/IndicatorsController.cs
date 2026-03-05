using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces;
using TradingBot.Middleware;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// REST endpoints for technical indicator calculation and querying.
    ///
    /// Endpoints:
    ///   POST /api/indicators/{symbol}/calculate  — fetch candles, run all indicators, save + return
    ///   GET  /api/indicators/{symbol}/latest     — most recent saved snapshot for a symbol
    ///   GET  /api/indicators/{symbol}/history    — last N saved snapshots for a symbol
    /// </summary>
    [ApiController]
    [Route("api/indicators")]
    [Authorize]
    public class IndicatorsController : ControllerBase
    {
        private readonly IIndicatorService _indicators;
        private readonly ILogger<IndicatorsController> _logger;

        public IndicatorsController(
            IIndicatorService indicators,
            ILogger<IndicatorsController> logger)
        {
            _indicators = indicators;
            _logger = logger;
        }

        /// <summary>
        /// Fetches the latest candles from Binance, calculates all indicators,
        /// persists the snapshot, and returns the result.
        /// </summary>
        /// <param name="symbol">Trading pair e.g. BTCUSDT</param>
        /// <param name="interval">Candle interval: 1m, 5m, 15m, 1h, 4h, 1d (default: 1h)</param>
        /// <param name="candleCount">Number of candles to use (default: 100, min: 50)</param>
        /// <returns>Fresh IndicatorSnapshot with RSI, EMA20/50, MACD, ATR, trend, S/R levels</returns>
        [HttpPost("{symbol}/calculate")]
        public async Task<IActionResult> Calculate(
            string symbol,
            [FromQuery] string interval = "1h",
            [FromQuery] int candleCount = 100)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            if (candleCount < 50)
                return BadRequest("candleCount must be at least 50 for reliable indicator results.");

            var validIntervals = new[] { "1m", "3m", "5m", "15m", "30m", "1h", "2h", "4h", "6h", "8h", "12h", "1d", "3d", "1w" };
            if (!validIntervals.Contains(interval.ToLower()))
                return BadRequest($"Invalid interval '{interval}'. Valid options: {string.Join(", ", validIntervals)}");

            try
            {
                var snapshot = await _indicators.CalculateIndicatorsAsync(
                    symbol.ToUpperInvariant(), interval, candleCount);

                return Ok(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to calculate indicators for {Symbol}", symbol);
                return StatusCode(500, $"Indicator calculation failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the most recently saved IndicatorSnapshot for a symbol.
        /// This reads from the database — it does NOT call Binance.
        /// Use POST /{symbol}/calculate to get fresh data.
        /// </summary>
        [HttpGet("{symbol}/latest")]
        public async Task<IActionResult> GetLatest(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            var snapshot = await _indicators.GetLatestSnapshotAsync(symbol.ToUpperInvariant());

            if (snapshot == null)
                return NotFound($"No indicator snapshot found for {symbol.ToUpperInvariant()}. " +
                                "Call POST /{symbol}/calculate first.");

            return Ok(snapshot);
        }

        /// <summary>
        /// Returns the last N saved IndicatorSnapshots for a symbol, newest first.
        /// Useful for charting indicator history or detecting trend changes over time.
        /// </summary>
        /// <param name="symbol">Trading pair e.g. BTCUSDT</param>
        /// <param name="count">Number of snapshots to return (default: 24, max: 200)</param>
        [HttpGet("{symbol}/history")]
        public async Task<IActionResult> GetHistory(
            string symbol,
            [FromQuery] int count = 24)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            if (count < 1 || count > 200)
                return BadRequest("count must be between 1 and 200.");

            var history = await _indicators.GetSnapshotHistoryAsync(
                symbol.ToUpperInvariant(), count);

            return Ok(new
            {
                symbol = symbol.ToUpperInvariant(),
                count = history.Count,
                snapshots = history
            });
        }
    }
}
