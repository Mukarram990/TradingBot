using TradingBot.Persistence;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace TradingBot.Services
{
    public class RiskManagementService : IRiskManagementService
    {
        private readonly TradingBotDbContext _db;

        public RiskManagementService(TradingBotDbContext db)
        {
            _db = db;
        }

        // ─── Helper: Load RiskProfile from DB ───────────────────────────────
        private async Task<RiskProfile> GetProfileAsync()
        {
            var profile = await _db.RiskProfiles.FirstOrDefaultAsync();
            return profile ?? throw new InvalidOperationException(
                "No RiskProfile found in the database. Run the application once to seed defaults.");
        }

        // ─── 1. Max trades per day ──────────────────────────────────────────
        public bool CanTradeToday()
        {
            var today = DateTime.UtcNow.Date;

            // Load synchronously (EF in-process) — acceptable for guard checks
            var profile = _db.RiskProfiles.FirstOrDefault()
                ?? throw new InvalidOperationException("RiskProfile not configured.");

            var tradeCount = _db.Trades
                .Count(t => t.EntryTime.Date == today);

            return tradeCount < profile.MaxTradesPerDay;
        }

        // ─── 2. Get daily starting balance (from today's first snapshot) ────
        public async Task<decimal> GetDailyStartingBalanceAsync()
        {
            var today = DateTime.UtcNow.Date;

            var snapshot = await _db.PortfolioSnapshots
                .Where(p => p.CreatedAt.Date == today)
                .OrderBy(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            // Return the first snapshot of the day, or 0 to signal "no baseline yet"
            return snapshot?.TotalBalanceUSDT ?? 0m;
        }

        // ─── 3. Daily loss limit check ───────────────────────────────────────
        public bool IsDailyLossExceeded(decimal currentBalance, decimal startingBalanceToday)
        {
            // If no baseline snapshot exists yet, allow trading
            if (startingBalanceToday <= 0)
                return false;

            var profile = _db.RiskProfiles.FirstOrDefault();
            if (profile == null) return false;

            var lossPercent = (startingBalanceToday - currentBalance) / startingBalanceToday;
            return lossPercent >= profile.MaxDailyLossPercent;   // e.g. 0.05 = 5%
        }

        // ─── 4. Stop loss validation ─────────────────────────────────────────
        public bool IsStopLossValid(decimal entryPrice, decimal stopLoss)
        {
            // Spot long only: SL must be below entry
            return stopLoss > 0 && stopLoss < entryPrice;
        }

        // ─── 5. Position sizing ──────────────────────────────────────────────
        public decimal CalculatePositionSize(
            decimal accountBalance,
            decimal entryPrice,
            decimal stopLoss)
        {
            if (!IsStopLossValid(entryPrice, stopLoss))
                throw new ArgumentException("Invalid Stop Loss: must be > 0 and < entry price.");

            var profile = _db.RiskProfiles.FirstOrDefault()
                ?? throw new InvalidOperationException("RiskProfile not configured.");

            // Risk amount = balance * MaxRiskPerTradePercent (e.g. 2%)
            var riskAmount = accountBalance * profile.MaxRiskPerTradePercent;

            // Units to buy = riskAmount / (entryPrice - stopLoss)
            var riskPerUnit = entryPrice - stopLoss;

            if (riskPerUnit <= 0)
                throw new ArgumentException("Risk per unit must be greater than zero.");

            var quantity = riskAmount / riskPerUnit;

            // Round to 6 decimal places (standard for crypto)
            return Math.Round(quantity, 6);
        }

        // ─── 6. Circuit breaker ──────────────────────────────────────────────
        public bool IsCircuitBreakerTriggered()
        {
            var profile = _db.RiskProfiles.FirstOrDefault();
            if (profile == null) return false;

            var today = DateTime.UtcNow.Date;

            // Count consecutive losing closed trades today
            var losingTradesToday = _db.Trades
                .Where(t => t.Status == TradeStatus.Closed
                         && t.EntryTime.Date == today
                         && t.PnL < 0)
                .Count();

            return losingTradesToday >= profile.CircuitBreakerLossCount;   // e.g. 3
        }
    }
}