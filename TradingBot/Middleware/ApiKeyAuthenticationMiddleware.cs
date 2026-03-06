using System.Security.Cryptography;
using System.Text;
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using TradingBot.Persistence;

namespace TradingBot.API.Middleware
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
        private sealed record CachedApiUser(int Id, string Username);

        private readonly RequestDelegate _next;
        private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
        private readonly IMemoryCache _cache;

        public ApiKeyAuthenticationMiddleware(
            RequestDelegate next,
            ILogger<ApiKeyAuthenticationMiddleware> logger,
            IMemoryCache cache)
        {
            _next = next;
            _logger = logger;
            _cache = cache;
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

                    _logger.LogDebug("API Key authenticated for user {UserId}", user.ID);
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

        private async Task<Domain.Entities.UserAccount?> ValidateApiKeyAsync(
            TradingBotDbContext db, string plainKey)
        {
            if (string.IsNullOrWhiteSpace(plainKey))
                return null;

            var hash = HashApiKey(plainKey);

            if (_cache.TryGetValue<CachedApiUser>($"api-key:{hash}", out var cached))
            {
                return new Domain.Entities.UserAccount
                {
                    ID = cached!.Id,
                    Username = cached.Username,
                    IsActive = true
                };
            }

            var user = await db.UserAccounts!
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.ApiKeyHash == hash && u.IsActive);

            if (user != null)
            {
                _cache.Set(
                    $"api-key:{hash}",
                    new CachedApiUser(user.ID, user.Username ?? $"User{user.ID}"),
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        SlidingExpiration = TimeSpan.FromMinutes(1)
                    });
            }

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
