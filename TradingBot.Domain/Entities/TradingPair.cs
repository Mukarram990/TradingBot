namespace TradingBot.Domain.Entities
{
    public class TradingPair : BaseEntity
    {
        public string Symbol { get; set; }           // BTCUSDT
        public string BaseAsset { get; set; }        // BTC
        public string QuoteAsset { get; set; }       // USDT

        public decimal MinQty { get; set; }
        public decimal StepSize { get; set; }

        public bool IsActive { get; set; }
    }
}
