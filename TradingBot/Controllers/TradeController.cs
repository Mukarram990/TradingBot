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

        // 1️⃣ Open new trade
        [HttpPost("open")]
        public async Task<IActionResult> OpenTrade([FromBody] TradeSignal signal)
        {
            var order = await _trade.OpenTradeAsync(signal);
            return Ok(order);
        }

        // 2️⃣ Close existing trade
        [HttpPost("close/{tradeId}")]
        public async Task<IActionResult> CloseTrade(int tradeId)
        {
            var order = await _trade.CloseTradeAsync(tradeId);
            return Ok(order);
        }
    }
}
