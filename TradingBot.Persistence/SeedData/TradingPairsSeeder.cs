using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.SeedData
{
    /// <summary>
    /// Seeds a default set of actively-watched trading pairs into the TradingPairs table
    /// on first startup. These are the pairs the MarketScannerService will scan.
    ///
    /// MinQty and StepSize values match the Binance testnet defaults for each symbol.
    /// They control minimum order quantity and lot-size precision for position sizing.
    ///
    /// Run once: if any rows exist the seeder returns immediately without changes.
    /// </summary>
    public static class TradingPairsSeeder
    {
        private static readonly List<TradingPair> DefaultPairs = new()
        {
            new TradingPair
            {
                Symbol     = "BTCUSDT",
                BaseAsset  = "BTC",
                QuoteAsset = "USDT",
                MinQty     = 0.00001m,
                StepSize   = 0.00001m,
                IsActive   = true
            },
            new TradingPair
            {
                Symbol     = "ETHUSDT",
                BaseAsset  = "ETH",
                QuoteAsset = "USDT",
                MinQty     = 0.0001m,
                StepSize   = 0.0001m,
                IsActive   = true
            },
            new TradingPair
            {
                Symbol     = "BNBUSDT",
                BaseAsset  = "BNB",
                QuoteAsset = "USDT",
                MinQty     = 0.001m,
                StepSize   = 0.001m,
                IsActive   = true
            },
            new TradingPair
            {
                Symbol     = "SOLUSDT",
                BaseAsset  = "SOL",
                QuoteAsset = "USDT",
                MinQty     = 0.01m,
                StepSize   = 0.01m,
                IsActive   = true
            },
            new TradingPair
            {
                Symbol     = "XRPUSDT",
                BaseAsset  = "XRP",
                QuoteAsset = "USDT",
                MinQty     = 0.1m,
                StepSize   = 0.1m,
                IsActive   = true
            },
            // Additional pairs — active = false by default so they don't get
            // scanned until explicitly enabled via the API.
            new TradingPair
            {
                Symbol     = "ADAUSDT",
                BaseAsset  = "ADA",
                QuoteAsset = "USDT",
                MinQty     = 1m,
                StepSize   = 1m,
                IsActive   = false
            },
            new TradingPair
            {
                Symbol     = "DOGEUSDT",
                BaseAsset  = "DOGE",
                QuoteAsset = "USDT",
                MinQty     = 1m,
                StepSize   = 1m,
                IsActive   = false
            },
        };

        /// <summary>
        /// Inserts default trading pairs if the table is empty.
        /// Safe to call on every startup — exits immediately if rows already exist.
        /// </summary>
        public static async Task SeedDefaultPairsAsync(TradingBotDbContext context)
        {
            bool hasAny = await context.TradingPairs!.AnyAsync();
            if (hasAny)
                return; // Already seeded — nothing to do.

            context.TradingPairs!.AddRange(DefaultPairs);
            await context.SaveChangesAsync();
        }
    }
}