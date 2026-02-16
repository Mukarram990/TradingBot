using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface ITradeExecutionService
    {
        Task<Order> ExecuteOrderAsync(TradeSignal signal);
        Task<bool> CancelOrderAsync(string orderId);
    }
}
