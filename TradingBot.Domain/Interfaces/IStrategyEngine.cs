using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    /// <summary>
    /// Pure rule-based strategy engine that converts an IndicatorSnapshot
    /// into a TradeSignal (or null if conditions are not met).
    ///
    /// This is intentionally separate from IStrategyService, which will be used
    /// in Phase 3 for AI-assisted signal generation. The StrategyEngine is the
    /// deterministic, technical-analysis layer that runs before AI validation.
    ///
    /// Signal generation pipeline:
    ///   IMarketScannerService.ScanPairAsync()
    ///       → IIndicatorService.CalculateIndicatorsAsync()   (Step 1)
    ///       → IStrategyEngine.EvaluateSignal()               (Step 3, this)
    ///       → [Phase 3] IAIService.ValidateSignal()
    ///       → ITradeExecutionService.OpenTradeAsync()
    /// </summary>
    public interface IStrategyEngine
    {
        /// <summary>
        /// Evaluates a pre-computed IndicatorSnapshot against all strategy rules.
        ///
        /// Returns a populated TradeSignal when all BUY conditions are satisfied
        /// and confidence >= 70. Returns null when conditions are not met or any
        /// hard disqualifier (overbought, downtrend, bearish MACD) fires.
        ///
        /// This method is synchronous because it performs pure computation with
        /// no I/O — it works entirely from the data already in the snapshot.
        /// </summary>
        /// <param name="snapshot">Indicator data computed by IndicatorCalculationService</param>
        /// <returns>A ready-to-execute TradeSignal, or null if no trade is warranted</returns>
        TradeSignal? EvaluateSignal(IndicatorSnapshot snapshot);

        /// <summary>
        /// Full pipeline: scans a symbol to get a fresh IndicatorSnapshot,
        /// evaluates the strategy rules, persists any generated signal to the
        /// TradeSignals table, and returns it.
        ///
        /// Returns null if conditions are not met (no signal generated).
        /// </summary>
        /// <param name="symbol">Trading pair, e.g. "BTCUSDT"</param>
        /// <param name="interval">Candle interval (default: "1h")</param>
        Task<TradeSignal?> EvaluateSignalForSymbolAsync(string symbol, string interval = "1h");

        /// <summary>
        /// Returns the most recently generated signals, newest first.
        /// Pass a symbol to filter by pair; leave null to get signals for all pairs.
        /// </summary>
        /// <param name="symbol">Optional filter — only signals for this pair</param>
        /// <param name="count">How many signals to return (default: 20)</param>
        Task<List<TradeSignal>> GetRecentSignalsAsync(string? symbol = null, int count = 20);
    }
}