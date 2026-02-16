using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Infrastructure.Binance
{
    /// <summary>
    /// Represents Binance API configuration.
    /// Bound from appsettings.json
    /// </summary>
    public class BinanceOptions
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }
}

