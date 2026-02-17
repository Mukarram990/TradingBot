using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface ITradeExecutionService
    {
        Task<Order> OpenTradeAsync(TradeSignal signal);

        Task<Order> CloseTradeAsync(int tradeId);

        Task<bool> CancelOrderAsync(string orderId);
    }
}
