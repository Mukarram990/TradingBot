using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces;
using TradingBot.Middleware;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// REST endpoints for the strategy engine.
    ///
    /// Endpoints:
    ///   POST /api/strategy/evaluate/{symbol}     — full pipeline: scan + indicators + evaluate
    ///   POST /api/strategy/scan-and-evaluate-all — evaluate all active pairs in one call
    ///   GET  /api/strategy/signals               — list recent generated signals
    ///   GET  /api/strategy/signals/{symbol}      — signals for one symbol
    /// </summary>
    [ApiController]
    [Route("api/strategy")]
    [Authorize]
    public class StrategyController : ControllerBase
    {
        private readonly IStrategyEngine _strategy;
        private readonly IMarketScannerService _scanner;
        private readonly ILogger<StrategyController> _logger;

        public StrategyController(
            IStrategyEngine strategy,
            IMarketScannerService scanner,
            ILogger<StrategyController> logger)
        {
            _strategy = strategy;
            _scanner = scanner;
            _logger = logger;
        }

        // ── POST /api/strategy/evaluate/{symbol} ─────────────────────────────

        /// <summary>
        /// Full pipeline for a single symbol:
        ///   1. Fetch candles from Binance
        ///   2. Calculate all indicators (RSI, EMA, MACD, ATR, etc.)
        ///   3. Save IndicatorSnapshot to DB
        ///   4. Evaluate strategy rules + confidence scoring
        ///   5. If signal generated (confidence ≥ 70): save to TradeSignals and return it
        ///   6. If no signal: return 200 with a "no signal" explanation
        ///
        /// Use this to manually trigger signal evaluation for any symbol on demand.
        /// Step 4 (SignalGenerationWorker) will call this automatically every 5 minutes.
        /// </summary>
        /// <param name="symbol">Trading pair, e.g. BTCUSDT</param>
        /// <param name="interval">Candle interval — 1m, 5m, 15m, 1h, 4h, 1d (default: 1h)</param>
        [HttpPost("evaluate/{symbol}")]
        public async Task<IActionResult> EvaluateSymbol(
            string symbol,
            [FromQuery] string interval = "1h")
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            try
            {
                var signal = await _strategy.EvaluateSignalForSymbolAsync(
                    symbol.ToUpperInvariant(), interval);

                if (signal == null)
                {
                    return Ok(new
                    {
                        symbol = symbol.ToUpperInvariant(),
                        signal = (object?)null,
                        result = "NO_SIGNAL",
                        message = "Conditions not met — no trade signal generated at this time."
                    });
                }

                return Ok(new
                {
                    symbol = signal.Symbol,
                    signal,
                    result = "SIGNAL_GENERATED",
                    message = $"BUY signal generated with confidence {signal.AIConfidence}/100."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Strategy evaluation failed for {Symbol}", symbol);
                return StatusCode(500, $"Evaluation failed for {symbol.ToUpperInvariant()}: {ex.Message}");
            }
        }

        // ── POST /api/strategy/scan-and-evaluate-all ─────────────────────────

        /// <summary>
        /// Runs the full evaluation pipeline for ALL active trading pairs.
        /// For each pair: scan → indicators → strategy rules → signal (if conditions met).
        ///
        /// Returns a summary with signals generated and pairs skipped.
        /// Pairs that fail individually (e.g. Binance API error) are skipped safely.
        ///
        /// This is what Step 4's SignalGenerationWorker will call automatically
        /// every 5 minutes. You can also trigger it manually here for testing.
        /// </summary>
        /// <param name="interval">Candle interval applied to all pairs (default: 1h)</param>
        [HttpPost("scan-and-evaluate-all")]
        public async Task<IActionResult> ScanAndEvaluateAll(
            [FromQuery] string interval = "1h")
        {
            var pairs = await _scanner.GetActivePairsAsync();

            var signals = new List<object>();
            var noSignals = new List<string>();
            var errors = new List<string>();

            foreach (var pair in pairs)
            {
                try
                {
                    var signal = await _strategy.EvaluateSignalForSymbolAsync(
                        pair.Symbol!, interval);

                    if (signal != null)
                    {
                        signals.Add(new
                        {
                            symbol = signal.Symbol,
                            confidence = signal.AIConfidence,
                            entry = signal.EntryPrice,
                            stopLoss = signal.StopLoss,
                            takeProfit = signal.TakeProfit,
                            signalId = signal.ID
                        });
                    }
                    else
                    {
                        noSignals.Add(pair.Symbol!);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(pair.Symbol ?? "unknown");
                    _logger.LogError(ex,
                        "Evaluation failed for {Symbol}", pair.Symbol);
                }
            }

            return Ok(new
            {
                evaluatedAt = DateTime.UtcNow,
                interval,
                totalPairs = pairs.Count,
                signalsFound = signals.Count,
                noSignalCount = noSignals.Count,
                errorCount = errors.Count,
                signals,
                noSignalPairs = noSignals,
                errorPairs = errors
            });
        }

        // ── GET /api/strategy/signals ─────────────────────────────────────────

        /// <summary>
        /// Returns recently generated trade signals, newest first.
        /// Optionally filter by symbol.
        /// </summary>
        /// <param name="symbol">Optional — filter to one symbol (e.g. BTCUSDT)</param>
        /// <param name="count">How many signals to return (default: 20, max: 100)</param>
        [HttpGet("signals")]
        public async Task<IActionResult> GetSignals(
            [FromQuery] string? symbol = null,
            [FromQuery] int count = 20)
        {
            if (count < 1 || count > 100)
                return BadRequest("count must be between 1 and 100.");

            var signals = await _strategy.GetRecentSignalsAsync(symbol, count);

            return Ok(new
            {
                count = signals.Count,
                filter = symbol?.ToUpperInvariant() ?? "all",
                signals
            });
        }

        // ── GET /api/strategy/signals/{symbol} ───────────────────────────────

        /// <summary>
        /// Returns the most recent signals for a specific symbol.
        /// Convenience wrapper around GET /signals?symbol=X.
        /// </summary>
        /// <param name="symbol">Trading pair, e.g. BTCUSDT</param>
        /// <param name="count">How many signals to return (default: 10)</param>
        [HttpGet("signals/{symbol}")]
        public async Task<IActionResult> GetSignalsForSymbol(
            string symbol,
            [FromQuery] int count = 10)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return BadRequest("Symbol is required.");

            if (count < 1 || count > 100)
                return BadRequest("count must be between 1 and 100.");

            var signals = await _strategy.GetRecentSignalsAsync(
                symbol.ToUpperInvariant(), count);

            if (!signals.Any())
                return NotFound(
                    $"No signals found for {symbol.ToUpperInvariant()}. " +
                    $"Run POST /api/strategy/evaluate/{symbol.ToUpperInvariant()} first.");

            return Ok(new
            {
                symbol = symbol.ToUpperInvariant(),
                count = signals.Count,
                signals
            });
        }
    }
}
