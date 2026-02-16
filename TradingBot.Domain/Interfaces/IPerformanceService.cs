using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Domain.Interfaces
{
    public interface IPerformanceService
    {
        Task<DailyPerformance> CalculateDailyPerformanceAsync();
        Task<PortfolioSnapshot> GetPortfolioSnapshotAsync();
    }
}
