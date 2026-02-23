using TradingBot.Persistence;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace TradingBot.Services
{
    public class RiskManagementService : IRiskManagementService
    {
        private readonly TradingBotDbContext _db;

        private const decimal MaxRiskPerTradePercent = 0.02m;      // 2%
        private const decimal DailyLossLimitPercent = 0.05m;       // 5%
        private const int MaxTradesPerDay = 5;

        public RiskManagementService(TradingBotDbContext db)
        {
            _db = db;
        }

        // 1️⃣ Check max trades per day
        public bool CanTradeToday()
        {
            var today = DateTime.UtcNow.Date;

            var tradeCount = _db.Trades
                .Count(t => t.EntryTime.Date == today);

            return tradeCount < MaxTradesPerDay;
        }

        // 1B️⃣ Get daily starting balance (NEW - PRIORITY 2 FIX)
        public async Task<decimal> GetDailyStartingBalanceAsync()
        {
            var today = DateTime.UtcNow.Date;
            
            var snapshot = await _db.PortfolioSnapshots
                .Where(p => p.CreatedAt.Date == today)
                .OrderBy(p => p.CreatedAt)
                .FirstOrDefaultAsync();
            
            if (snapshot == null)
            {
                // No snapshot yet - return a safe value, caller should create snapshot
                return 10000m; // Default - should be overridden
            }
            
            return snapshot.TotalBalanceUSDT;
        }

        // 2️⃣ Check daily loss limit
        public bool IsDailyLossExceeded(decimal currentBalance, decimal startingBalanceToday)
        {
            if (startingBalanceToday <= 0)
                return false;

            var lossPercent = (startingBalanceToday - currentBalance) / startingBalanceToday;

            return lossPercent >= DailyLossLimitPercent;
        }

        // 3️⃣ Validate stop loss
        public bool IsStopLossValid(decimal entryPrice, decimal stopLoss)
        {
            return stopLoss < entryPrice; // spot long only
        }

        // 4️⃣ Calculate position size (core logic)
        public decimal CalculatePositionSize(
            decimal accountBalance,
            decimal entryPrice,
            decimal stopLoss)
        {
            if (!IsStopLossValid(entryPrice, stopLoss))
                throw new Exception("Invalid Stop Loss.");

            var riskAmount = accountBalance * MaxRiskPerTradePercent;

            var riskPerUnit = entryPrice - stopLoss;

            if (riskPerUnit <= 0)
                throw new Exception("Invalid risk per unit.");

            var quantity = riskAmount / riskPerUnit;

            return Math.Round(quantity, 6);
        }

        // 5️⃣ Simple circuit breaker
        public bool IsCircuitBreakerTriggered()
        {
            var today = DateTime.UtcNow.Date;

            var losingTrades = _db.Trades
                .Where(o => o.CreatedAt.Date == today &&
                            o.Status == TradeStatus.Closed &&
                            o.PnL < 0)
                .Count();

            return losingTrades >= 3;
        }
    }
}
