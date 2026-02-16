using System.Collections.Generic;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface IMarketDataService
    {
        Task<IEnumerable<Candle>> GetRecentCandlesAsync(string symbol, int limit);
        Task<decimal> GetCurrentPriceAsync(string symbol);
    }
}
