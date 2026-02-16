using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/trade")]
    public class TradeController : ControllerBase
    {
        private readonly ITradeExecutionService _trade;

        public TradeController(ITradeExecutionService trade)
        {
            _trade = trade;
        }

        [HttpPost("market")]
        public async Task<IActionResult> PlaceMarketOrder([FromBody] TradeSignal signal)
        {
            var order = await _trade.ExecuteOrderAsync(signal);
            return Ok(order);
        }
    }

}
