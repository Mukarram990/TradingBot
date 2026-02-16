using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface IAIService
    {
        Task<TradeSignal> GenerateSignalAsync(string symbol);
        Task<string> AnalyzeMarketAsync(string symbol);
    }
}
