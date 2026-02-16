using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface IPortfolioService
    {
        Task<Position> GetPositionAsync(string symbol);
        Task UpdatePositionAsync(Position position);
    }
}
