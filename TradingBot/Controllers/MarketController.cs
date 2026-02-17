using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Interfaces;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/market")]
    public class MarketController(IMarketDataService market) : ControllerBase
    {
        private readonly IMarketDataService _market = market;

        [HttpGet("price/{symbol}")]
        public async Task<IActionResult> GetPrice(string symbol)
        {
            var price = await _market.GetCurrentPriceAsync(symbol);
            return Ok(price);
        }
        [HttpGet("candles")]
        public async Task<IActionResult> GetCandles([FromQuery] string symbol, [FromQuery] string interval, [FromQuery] int limit = 100)
        {
            var candles = await _market.GetRecentCandlesAsync(symbol, limit, interval);
            return Ok(candles);
        }

    }
}
