using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/market")]
    public class MarketController : ControllerBase
    {
        private readonly IMarketDataService _market;

        public MarketController(IMarketDataService market)
        {
            _market = market;
        }

        [HttpGet("price/{symbol}")]
        public async Task<IActionResult> GetPrice(string symbol)
        {
            var price = await _market.GetCurrentPriceAsync(symbol);
            return Ok(price);
        }
    }

}
