using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.SeedData
{
    public static class DefaultAdminSeeder
    {
        public static async Task SeedAsync(
            TradingBotDbContext db,
            IConfiguration config,
            ILogger logger)
        {
            var existing = await db.UserAccounts!.FirstOrDefaultAsync();
            if (existing != null)
                return;

            var username = config["Auth:DefaultAdminUsername"] ?? "admin";
            var plainApiKey = config["Auth:DefaultAdminApiKey"];

            if (string.IsNullOrWhiteSpace(plainApiKey))
                plainApiKey = config["ADMIN_API_KEY"];

            if (string.IsNullOrWhiteSpace(plainApiKey))
                plainApiKey = "6b30383ca83343349d53c0e931db97fd";

            var user = new UserAccount
            {
                Username = username,
                IsActive = true,
                ApiKeyHash = HashApiKey(plainApiKey),
                ApiKeyGeneratedAt = DateTime.UtcNow
            };

            db.UserAccounts!.Add(user);
            await db.SaveChangesAsync();

            logger.LogWarning(
                "Default admin account seeded. Username={Username}. Configure Auth:DefaultAdminApiKey (or ADMIN_API_KEY) and rotate immediately.",
                username);
        }

        private static string HashApiKey(string plainKey)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(plainKey);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
