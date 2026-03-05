using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.EntityFrameworkCore;
using TradingBot.Persistence;

namespace TradingBot.Middleware
{
    /// <summary>
    /// API Key authentication middleware.
    /// 
    /// Expected header format: X-API-KEY: {plaintext-key}
    /// Validates against hashed keys in UserAccount.ApiKeyHash.
    /// 
    /// If valid, sets HttpContext.User with user info in claims.
    /// If invalid, returns 401 Unauthorized.
    /// </summary>
    public class ApiKeyAuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

        public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, TradingBotDbContext db)
        {
            // Let CORS preflight requests pass through unchallenged.
            if (HttpMethods.IsOptions(context.Request.Method))
            {
                await _next(context);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            // Optional internal bypass for non-user service calls originating from localhost.
            var internalBypass = string.Equals(
                context.Request.Headers["X-INTERNAL-REQUEST"].FirstOrDefault(),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (internalBypass
                && context.Connection.RemoteIpAddress != null
                && IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
            {
                await _next(context);
                return;
            }

            var headerKey = context.Request.Headers["X-API-KEY"].FirstOrDefault();

            // Backward compatibility: support Authorization: ApiKey {key}
            if (string.IsNullOrWhiteSpace(headerKey))
            {
                var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(authHeader) &&
                    authHeader.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
                {
                    headerKey = authHeader["ApiKey ".Length..].Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(headerKey))
            {
                var user = await ValidateApiKeyAsync(db, headerKey);

                if (user != null)
                {
                    // Set user in HttpContext for controllers
                    var identity = new System.Security.Principal.GenericIdentity(user.Username ?? $"User{user.ID}");
                    var principal = new System.Security.Principal.GenericPrincipal(identity, null);
                    context.User = principal;

                    // Also add as context item for DI access
                    context.Items["UserId"] = user.ID;
                    context.Items["Username"] = user.Username;

                    _logger.LogInformation("API Key authenticated for user {UserId}", user.ID);
                }
                else
                {
                    _logger.LogWarning("Invalid API Key attempt from {RemoteIp}", context.Connection.RemoteIpAddress);
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "Invalid API key." });
                    return;
                }
            }
            else
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Unauthorized",
                    message = "API key required. Supply header X-API-KEY."
                });
                return;
            }

            await _next(context);
        }

        private async Task<TradingBot.Domain.Entities.UserAccount?> ValidateApiKeyAsync(
            TradingBotDbContext db, string plainKey)
        {
            if (string.IsNullOrWhiteSpace(plainKey))
                return null;

            var hash = HashApiKey(plainKey);
            var user = (await db.UserAccounts!
                .Where(u => u.ApiKeyHash == hash && u.IsActive)
                .ToListAsync())
                .FirstOrDefault();

            return user;
        }

        /// <summary>
        /// Hash an API key using SHA256.
        /// </summary>
        public static string HashApiKey(string plainKey)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(plainKey);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Generate a random API key.
        /// </summary>
        public static string GenerateApiKey()
        {
            var randomBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }
    }
}
