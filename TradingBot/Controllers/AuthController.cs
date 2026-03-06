using Microsoft.AspNetCore.Mvc;
using TradingBot.API.Middleware;
using TradingBot.Persistence;

namespace TradingBot.API.Controllers
{
    /// <summary>
    /// API Key management for users.
    /// 
    ///   POST /api/auth/generate-key     — generate new API key for authenticated user
    ///   POST /api/auth/revoke-key       — revoke current API key
    /// </summary>
    [ApiController]
    [Route("api/auth")]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly TradingBotDbContext _db;
        private readonly ILogger<AuthController> _logger;

        public AuthController(TradingBotDbContext db, ILogger<AuthController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Generate a new API key for the authenticated user.
        /// IMPORTANT: Save the returned key securely — it cannot be retrieved again!
        /// </summary>
        [HttpPost("generate-key")]
        [Authorize]
        public async Task<IActionResult> GenerateApiKey()
        {
            var userId = (int?)HttpContext.Items["UserId"];
            if (!userId.HasValue)
                return BadRequest(new { error = "User context missing" });

            var user = await _db.UserAccounts!.FindAsync(userId.Value);
            if (user == null)
                return NotFound(new { error = "User not found" });

            // Generate new key
            var plainKey = ApiKeyAuthenticationMiddleware.GenerateApiKey();
            var hashedKey = ApiKeyAuthenticationMiddleware.HashApiKey(plainKey);

            // Update user
            user.ApiKeyHash = hashedKey;
            user.ApiKeyGeneratedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("New API key generated for user {UserId}", userId.Value);

            return Ok(new
            {
                message = "API key generated successfully.",
                apiKey = plainKey,
                warning = "⚠️ Save this key securely. You won't be able to see it again!",
                generatedAt = DateTime.UtcNow,
                usage = "X-API-KEY: {apiKey}"
            });
        }

        /// <summary>
        /// Revoke the current API key.
        /// </summary>
        [HttpPost("revoke-key")]
        [Authorize]
        public async Task<IActionResult> RevokeApiKey()
        {
            var userId = (int?)HttpContext.Items["UserId"];
            if (!userId.HasValue)
                return BadRequest(new { error = "User context missing" });

            var user = await _db.UserAccounts!.FindAsync(userId.Value);
            if (user == null)
                return NotFound(new { error = "User not found" });

            user.ApiKeyHash = null;
            user.ApiKeyGeneratedAt = null;
            await _db.SaveChangesAsync();

            _logger.LogInformation("API key revoked for user {UserId}", userId.Value);

            return Ok(new
            {
                message = "API key revoked successfully.",
                revokedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Check current authentication status.
        /// </summary>
        [HttpGet("status")]
        [Authorize]
        public IActionResult GetAuthStatus()
        {
            var username = (string?)HttpContext.Items["Username"] ?? "Unknown";
            var userId = (int?)HttpContext.Items["UserId"];

            return Ok(new
            {
                authenticated = true,
                userId,
                username,
                checkedAt = DateTime.UtcNow
            });
        }
    }
}

