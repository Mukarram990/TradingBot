using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.SeedData
{
    public static class StrategySeeder
    {
        public static async Task SeedDefaultStrategyAsync(TradingBotDbContext context)
        {
            var existing = await context.Strategies!.FirstOrDefaultAsync();
            if (existing != null) return;

            var def = new
            {
                type = "ema_crossover",
                weight = 1.0m,
                fastEma = 20,
                slowEma = 50,
                useRsi = true,
                rsiMin = 45m,
                rsiMax = 68m,
                useMacd = true,
                macdMin = 0m,
                useAtr = false,
                atrMin = 0m,
                requireVolumeSpike = true,
                minConfidence = 70
            };

            var strategy = new Strategy
            {
                Name = "Spot EMA 20/50 Momentum Pullback",
                Description = JsonSerializer.Serialize(def),
                Version = "ema_crossover",
                IsActive = true,
                MinConfidenceRequired = 70
            };

            context.Strategies!.Add(strategy);
            await context.SaveChangesAsync();
        }
    }
}
