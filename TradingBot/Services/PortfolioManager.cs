using TradingBot.Domain.Entities;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance;
using TradingBot.Persistence;

namespace TradingBot.API.Services
{
    public class PortfolioManager
    {
        private readonly BinanceAccountService _accountService;
        private readonly IMarketDataService _marketService;
        private readonly TradingBotDbContext _db;

        public PortfolioManager(BinanceAccountService accountService, IMarketDataService marketService, TradingBotDbContext db)
        {
            _accountService = accountService;
            _marketService = marketService;
            _db = db;
        }

        public async Task<PortfolioSnapshot> CreateSnapshotAsync()
        {
            var account = await _accountService.GetAccountInfoAsync();

            decimal totalUsdtValue = 0m;

            foreach (var balance in account.Balances)
            {
                var free = decimal.Parse(balance.Free);
                if (free == 0) continue;

                if (balance.Asset == "USDT")
                {
                    totalUsdtValue += free;
                }
                else
                {
                    var symbol = balance.Asset + "USDT";

                    try
                    {
                        var price = await _marketService.GetCurrentPriceAsync(symbol);
                        totalUsdtValue += free * price;
                    }
                    catch
                    {
                        // ignore assets that don't have USDT pair
                    }
                }
            }

            var openPositions = _db.Orders.Count(o => o.Status == Domain.Enums.TradeStatus.Open);

            var snapshot = new PortfolioSnapshot
            {
                TotalBalanceUSDT = totalUsdtValue,
                TotalUnrealizedPnL = 0,
                DailyPnL = 0,
                TotalOpenPositions = openPositions,
                CreatedAt = DateTime.UtcNow
            };

            _db.PortfolioSnapshots.Add(snapshot);
            await _db.SaveChangesAsync();

            return snapshot;
        }
    }
}
