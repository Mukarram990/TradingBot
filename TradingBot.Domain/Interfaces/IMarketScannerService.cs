using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    /// <summary>
    /// Scans trading pairs for technical indicator data.
    ///
    /// Responsibilities:
    ///   - Provide the list of actively watched pairs (from DB or defaults)
    ///   - Scan a single pair: fetch candles → calculate all indicators → persist snapshot
    ///   - Scan all active pairs in one sweep
    ///   - Manage which pairs are active/inactive
    /// </summary>
    public interface IMarketScannerService
    {
        /// <summary>
        /// Returns all trading pairs where IsActive = true.
        /// Falls back to a hardcoded default list (BTC, ETH, BNB, SOL, XRP)
        /// if the TradingPairs table is empty.
        /// </summary>
        Task<List<TradingPair>> GetActivePairsAsync();

        /// <summary>
        /// Fetches candles from Binance for the given symbol, runs all indicator
        /// calculations (RSI, EMA, MACD, ATR, Volume Spike, S/R), persists the
        /// IndicatorSnapshot, and returns it.
        /// </summary>
        /// <param name="symbol">e.g. "BTCUSDT"</param>
        /// <param name="interval">Candle interval, default "1h"</param>
        /// <param name="candleCount">Number of candles to use, default 100</param>
        Task<IndicatorSnapshot> ScanPairAsync(string symbol, string interval = "1h", int candleCount = 100);

        /// <summary>
        /// Runs ScanPairAsync for every active pair sequentially.
        /// Pairs that fail (e.g. Binance API error) are skipped and logged;
        /// the rest of the pairs are still processed.
        /// Returns snapshots only for pairs that succeeded.
        /// </summary>
        /// <param name="interval">Candle interval applied to all pairs</param>
        /// <param name="candleCount">Number of candles per pair</param>
        Task<List<IndicatorSnapshot>> ScanAllPairsAsync(string interval = "1h", int candleCount = 100);

        /// <summary>
        /// Activates an existing pair (sets IsActive = true).
        /// If the symbol does not exist in TradingPairs it is inserted first.
        /// </summary>
        Task<TradingPair> ActivatePairAsync(string symbol);

        /// <summary>
        /// Deactivates a pair (sets IsActive = false).
        /// Returns false if the symbol was not found.
        /// </summary>
        Task<bool> DeactivatePairAsync(string symbol);
    }
}