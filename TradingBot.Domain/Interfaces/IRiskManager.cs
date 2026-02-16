using System.Threading.Tasks;

namespace TradingBot.Domain.Interfaces
{
    public interface IRiskManager
    {
        Task<bool> ValidateTradeAsync(string symbol, decimal quantity, decimal price);
        Task<decimal> CalculatePositionSizeAsync(string symbol, decimal riskPercentage);
    }
}
