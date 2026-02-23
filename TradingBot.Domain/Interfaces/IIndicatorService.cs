using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    /// <summary>
    /// Calculates technical indicators from live candle data and persists snapshots.
    /// </summary>
    public interface IIndicatorService
    {
        /// <summary>
        /// Fetches candles from Binance, calculates all indicators (RSI, EMA, MACD, ATR,
        /// Volume Spike, Support/Resistance), saves the result to the DB, and returns it.
        /// </summary>
        /// <param name="symbol">Trading pair, e.g. "BTCUSDT"</param>
        /// <param name="interval">Candle interval, e.g. "1h", "15m", "4h"</param>
        /// <param name="candleCount">How many candles to fetch (minimum 100 recommended)</param>
        Task<IndicatorSnapshot> CalculateIndicatorsAsync(string symbol, string interval = "1h", int candleCount = 100);

        /// <summary>
        /// Returns the most recently saved IndicatorSnapshot for the given symbol.
        /// Returns null if no snapshot has been calculated yet.
        /// </summary>
        Task<IndicatorSnapshot?> GetLatestSnapshotAsync(string symbol);

        /// <summary>
        /// Returns the last N saved IndicatorSnapshots for a symbol, newest first.
        /// Useful for tracking indicator history over time.
        /// </summary>
        Task<List<IndicatorSnapshot>> GetSnapshotHistoryAsync(string symbol, int count = 24);
    }
}