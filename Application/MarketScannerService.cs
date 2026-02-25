using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Application
{
    /// <summary>
    /// Orchestrates the market scanning pipeline:
    ///
    ///   1. GetActivePairsAsync   — read TradingPairs (IsActive = true) from DB.
    ///                              Falls back to a hardcoded list if the table is empty.
    ///   2. ScanPairAsync         — for one symbol: call IIndicatorService which fetches
    ///                              candles, calculates all indicators, persists and returns
    ///                              the IndicatorSnapshot.
    ///   3. ScanAllPairsAsync     — iterate all active pairs, call ScanPairAsync on each.
    ///                              Individual failures are caught and logged; scanning
    ///                              continues for the remaining pairs.
    ///   4. ActivatePairAsync     — add or re-enable a symbol in the TradingPairs table.
    ///   5. DeactivatePairAsync   — disable a symbol so it is excluded from future scans.
    /// </summary>
    public class MarketScannerService : IMarketScannerService
    {
        private readonly TradingBotDbContext _db;
        private readonly IIndicatorService _indicators;
        private readonly ILogger<MarketScannerService> _logger;

        // Fallback list used when TradingPairs table is empty (e.g. first run
        // before the seeder has had a chance to run, or in unit tests).
        private static readonly IReadOnlyList<string> FallbackSymbols =
            new[] { "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT" };

        public MarketScannerService(
            TradingBotDbContext db,
            IIndicatorService indicators,
            ILogger<MarketScannerService> logger)
        {
            _db = db;
            _indicators = indicators;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        // 1. GetActivePairsAsync
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public async Task<List<TradingPair>> GetActivePairsAsync()
        {
            var pairs = await _db.TradingPairs!
                .Where(p => p.IsActive)
                .OrderBy(p => p.Symbol)
                .AsNoTracking()
                .ToListAsync();

            if (pairs.Count > 0)
            {
                _logger.LogInformation(
                    "GetActivePairs: found {Count} active pairs in DB: {Symbols}",
                    pairs.Count,
                    string.Join(", ", pairs.Select(p => p.Symbol)));
                return pairs;
            }

            // DB is empty — return in-memory fallback objects so callers always
            // have something to work with. These are NOT saved to the DB here;
            // the TradingPairsSeeder handles persistence at startup.
            _logger.LogWarning(
                "TradingPairs table is empty. Using fallback list: {Symbols}",
                string.Join(", ", FallbackSymbols));

            return FallbackSymbols.Select(s => new TradingPair
            {
                Symbol = s,
                IsActive = true,
                BaseAsset = s.Replace("USDT", ""),
                QuoteAsset = "USDT"
            }).ToList();
        }

        // ════════════════════════════════════════════════════════════════════
        // 2. ScanPairAsync
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public async Task<IndicatorSnapshot> ScanPairAsync(
            string symbol,
            string interval = "1h",
            int candleCount = 100)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));

            symbol = symbol.ToUpperInvariant();

            _logger.LogInformation(
                "Scanning {Symbol} [{Interval}, {Count} candles]",
                symbol, interval, candleCount);

            // IIndicatorService handles: fetch candles → calculate → persist → return.
            var snapshot = await _indicators.CalculateIndicatorsAsync(
                symbol, interval, candleCount);

            _logger.LogInformation(
                "Scan complete for {Symbol}: RSI={RSI:F1}, Trend={Trend}, MACD={MACD:F4}",
                symbol, snapshot.RSI, snapshot.Trend, snapshot.MACD);

            return snapshot;
        }

        // ════════════════════════════════════════════════════════════════════
        // 3. ScanAllPairsAsync
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public async Task<List<IndicatorSnapshot>> ScanAllPairsAsync(
            string interval = "1h",
            int candleCount = 100)
        {
            var pairs = await GetActivePairsAsync();

            _logger.LogInformation(
                "Starting full market scan: {Count} pair(s) [{Interval}]",
                pairs.Count, interval);

            var results = new List<IndicatorSnapshot>();
            var failures = new List<string>();

            foreach (var pair in pairs)
            {
                try
                {
                    var snapshot = await ScanPairAsync(pair.Symbol!, interval, candleCount);
                    results.Add(snapshot);
                }
                catch (Exception ex)
                {
                    // One pair failing must not abort the whole scan.
                    failures.Add(pair.Symbol ?? "unknown");
                    _logger.LogError(
                        ex,
                        "Scan failed for {Symbol} — skipping. Error: {Message}",
                        pair.Symbol, ex.Message);
                }
            }

            _logger.LogInformation(
                "Full scan complete. Success: {Success}/{Total}. Failed: {Failed}",
                results.Count,
                pairs.Count,
                failures.Count > 0 ? string.Join(", ", failures) : "none");

            return results;
        }

        // ════════════════════════════════════════════════════════════════════
        // 4. ActivatePairAsync
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public async Task<TradingPair> ActivatePairAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                throw new ArgumentException("Symbol cannot be empty.", nameof(symbol));

            symbol = symbol.ToUpperInvariant();

            var existing = await _db.TradingPairs!
                .FirstOrDefaultAsync(p => p.Symbol == symbol);

            if (existing != null)
            {
                if (existing.IsActive)
                {
                    _logger.LogInformation("{Symbol} is already active.", symbol);
                    return existing;
                }

                existing.IsActive = true;
                existing.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                _logger.LogInformation("{Symbol} re-activated.", symbol);
                return existing;
            }

            // Symbol not in DB yet — create a minimal record.
            var newPair = new TradingPair
            {
                Symbol = symbol,
                BaseAsset = symbol.Replace("USDT", ""),
                QuoteAsset = "USDT",
                IsActive = true,
                MinQty = 0.001m,   // conservative default; user can update via API
                StepSize = 0.001m
            };

            _db.TradingPairs!.Add(newPair);
            await _db.SaveChangesAsync();

            _logger.LogInformation("New pair {Symbol} created and activated.", symbol);
            return newPair;
        }

        // ════════════════════════════════════════════════════════════════════
        // 5. DeactivatePairAsync
        // ════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public async Task<bool> DeactivatePairAsync(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return false;

            symbol = symbol.ToUpperInvariant();

            var existing = await _db.TradingPairs!
                .FirstOrDefaultAsync(p => p.Symbol == symbol);

            if (existing == null)
            {
                _logger.LogWarning("DeactivatePair: {Symbol} not found.", symbol);
                return false;
            }

            existing.IsActive = false;
            existing.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("{Symbol} deactivated.", symbol);
            return true;
        }
    }
}