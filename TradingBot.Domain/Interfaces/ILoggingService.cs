using System.Threading.Tasks;

namespace TradingBot.Domain.Interfaces
{
    public interface ILoggingService
    {
        Task LogInfoAsync(string message);
        Task LogWarningAsync(string message);
        Task LogErrorAsync(string message);
    }
}
