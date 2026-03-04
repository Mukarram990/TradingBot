// ==============================================================================
// FILE: TradingBot.Infrastructure/Binance/BinanceVwapHelper.cs
// NEW helper class — add to the Infrastructure/Binance folder.
// ==============================================================================
using System.Text.Json;

namespace TradingBot.Infrastructure.Binance
{
    /// <summary>
    /// Calculates the Volume-Weighted Average Price (VWAP) from Binance order fills.
    ///
    /// WHY THIS MATTERS:
    ///   Large MARKET orders on Binance are often filled across multiple price levels
    ///   (partial fills). The old code captured only fills[0] — the first fill price.
    ///   This understates the true cost basis for multi-fill orders, leading to
    ///   incorrect PnL calculations.
    ///
    /// VWAP FORMULA:
    ///   executedPrice = sum(price_i * qty_i) / sum(qty_i)
    ///
    /// USAGE:
    ///   Replace the fills[0] block in BinanceTradeExecutionService.OpenTradeAsync:
    ///
    ///   // OLD — only first fill:
    ///   if (result.TryGetProperty("fills", out var fills) && fills.GetArrayLength() > 0)
    ///       executedPrice = decimal.Parse(fills[0].GetProperty("price").GetString()!,...);
    ///
    ///   // NEW — VWAP across all fills:
    ///   executedPrice = BinanceVwapHelper.CalculateVwap(result);
    ///
    ///   Apply the same replacement in CloseTradeAsync.
    /// </summary>
    public static class BinanceVwapHelper
    {
        /// <summary>
        /// Reads the "fills" array from a Binance order response and returns the
        /// volume-weighted average price. Returns null if fills are missing/empty.
        /// </summary>
        public static decimal? CalculateVwap(JsonElement orderResult)
        {
            if (!orderResult.TryGetProperty("fills", out var fills))
                return null;

            int count = fills.GetArrayLength();
            if (count == 0) return null;

            decimal totalValue = 0m;
            decimal totalQty = 0m;

            for (int i = 0; i < count; i++)
            {
                var fill = fills[i];

                if (!fill.TryGetProperty("price", out var priceProp) ||
                    !fill.TryGetProperty("qty", out var qtyProp))
                    continue;

                decimal price = decimal.Parse(
                    priceProp.GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);

                decimal qty = decimal.Parse(
                    qtyProp.GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture);

                totalValue += price * qty;
                totalQty += qty;
            }

            return totalQty > 0 ? Math.Round(totalValue / totalQty, 8) : null;
        }
    }
}