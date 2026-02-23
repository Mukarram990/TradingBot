using TradingBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace TradingBot.Persistence.SeedData
{
    public static class RiskProfileSeeder
    {
        public static async Task SeedDefaultRiskProfileAsync(TradingBotDbContext context)
        {
            // Check if default risk profile exists
            var existing = await context.RiskProfiles.FirstOrDefaultAsync();
            
            if (existing != null)
                return;  // Already seeded

            var defaultProfile = new RiskProfile
            {
                MaxRiskPerTradePercent = 0.02m,      // 2%
                MaxDailyLossPercent = 0.05m,         // 5%
                MaxTradesPerDay = 5,
                CircuitBreakerLossCount = 3,
                IsEnabled = true
            };

            context.RiskProfiles.Add(defaultProfile);
            await context.SaveChangesAsync();
        }
    }
}
