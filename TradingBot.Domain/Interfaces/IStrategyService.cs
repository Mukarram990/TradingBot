using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface IStrategyService
    {
        Task<TradeSignal> EvaluateStrategyAsync(string symbol);
    }
}
