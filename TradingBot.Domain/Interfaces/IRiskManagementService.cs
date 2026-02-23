namespace TradingBot.Domain.Interfaces
{
    public interface IRiskManagementService
    {
        bool CanTradeToday();
        Task<decimal> GetDailyStartingBalanceAsync();
        bool IsDailyLossExceeded(decimal currentBalance, decimal startingBalanceToday);
        bool IsStopLossValid(decimal entryPrice, decimal stopLoss);
        decimal CalculatePositionSize(decimal accountBalance, decimal entryPrice, decimal stopLoss);
        bool IsCircuitBreakerTriggered();
    }
}
