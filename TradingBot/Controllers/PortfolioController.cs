using Microsoft.AspNetCore.Mvc;
using TradingBot.Services;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/portfolio")]
    public class PortfolioController(PortfolioManager manager) : ControllerBase
    {
        private readonly PortfolioManager _manager = manager;

        [HttpPost("snapshot")]
        public async Task<IActionResult> CreateSnapshot()
        {
            var result = await _manager.CreateSnapshotAsync();
            return Ok(result);
        }
    }

}
