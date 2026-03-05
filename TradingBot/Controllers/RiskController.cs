using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Persistence;
using TradingBot.Middleware;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/risk")]
    [Authorize]
    public class RiskController : ControllerBase
    {
        private readonly TradingBotDbContext _db;

        public RiskController(TradingBotDbContext db)
        {
            _db = db;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetRiskProfile()
        {
            var profile = await _db.RiskProfiles.AsNoTracking().FirstOrDefaultAsync();
            if (profile == null)
                return NotFound("No risk profile configured");

            return Ok(profile);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateRiskProfile([FromBody] RiskProfile profile)
        {
            var existing = await _db.RiskProfiles.FirstOrDefaultAsync();

            if (existing == null)
            {
                _db.RiskProfiles.Add(profile);
            }
            else
            {
                existing.MaxRiskPerTradePercent = profile.MaxRiskPerTradePercent;
                existing.MaxDailyLossPercent = profile.MaxDailyLossPercent;
                existing.MaxTradesPerDay = profile.MaxTradesPerDay;
                existing.CircuitBreakerLossCount = profile.CircuitBreakerLossCount;
                existing.IsEnabled = profile.IsEnabled;
                existing.UpdatedAt = DateTime.UtcNow;

                _db.RiskProfiles.Update(existing);
            }

            await _db.SaveChangesAsync();
            return Ok(profile);
        }
    }
}

