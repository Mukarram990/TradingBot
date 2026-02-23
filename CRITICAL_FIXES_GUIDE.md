# TradingBot - Critical Fixes & Implementation Guide

## 🔴 PRIORITY 1: Fix BaseEntity ID Type (BLOCKING)

### Current Problem:
```csharp
// ❌ WRONG - int doesn't match database uniqueidentifier
public abstract class BaseEntity
{
    public int ID { get; set; }
}
```

### Solution:

#### Step 1: Update BaseEntity to use Guid
```csharp
// TradingBot.Domain\Entities\BaseEntity.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradingBot.Domain.Entities
{
    public abstract class BaseEntity
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid ID { get; set; } = Guid.NewGuid();
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
```

#### Step 2: Update DbContext to handle Guid properly
```csharp
// TradingBot.Persistence\TradingBotDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    // ... existing indexes ...

    // Add this for Guid key generation
    foreach (var entity in modelBuilder.Model.GetEntityTypes())
    {
        var primaryKey = entity.FindPrimaryKey();
        if (primaryKey?.Properties.Count == 1)
        {
            var property = primaryKey.Properties[0];
            if (property.ClrType == typeof(Guid))
            {
                property.SetDefaultValueSql("NEWID()");  // SQL Server auto-generates GUID
            }
        }
    }

    // ... rest of configuration ...
}
```

#### Step 3: Fix Order.cs TradeId Type
```csharp
// TradingBot.Domain\Entities\Order.cs - Update this line
public Guid TradeId { get; set; }  // ✅ Changed from int

// And the navigation property
public Trade Trade { get; set; } = null!;
```

#### Step 4: Update any code that uses ID
In BinanceTradeExecutionService:
```csharp
// Change this:
TradeId = trade.ID  // ❌ Wrong - trade.ID is now Guid

// To this:
TradeId = trade.ID  // ✅ Still the same, but now it's Guid type
```

#### Step 5: Create Migration
```bash
cd TradingBot.Persistence
dotnet ef migrations add FixBaseEntityIdTypeToGuid
dotnet ef database update
```

---

## 🔴 PRIORITY 2: Move Credentials Out of appsettings.json

### Current Problem:
```json
{
  "Binance": {
    "ApiKey": "w6o4xpvaZkdQI2f6jqVO2Bf15sQ3OvNIdz0UlbzqEV4I7wOksfzF0Fez6450rzfs",
    "ApiSecret": "ltzRe42DPd1y4gDGEGojf2zlGBpQnWsxPWJttTLP60rTRMC8rZaBjSkygmXqwgIY"
  }
}
```

❌ **NEVER COMMIT TO GIT!**

### Solution:

#### Step 1: Clean appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\MSSQLSERVER01;Database=TradingBotDb;Trusted_Connection=True;TrustServerCertificate=TRUE;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Binance": {
    "BaseUrl": "https://testnet.binance.vision"
  },
  "AllowedHosts": "*"
}
```

#### Step 2: Use User Secrets (Local Development)
```bash
# Initialize user secrets (one time)
cd TradingBot
dotnet user-secrets init

# Set your actual credentials
dotnet user-secrets set "Binance:ApiKey" "your-actual-key-here"
dotnet user-secrets set "Binance:ApiSecret" "your-actual-secret-here"

# Verify
dotnet user-secrets list
```

#### Step 3: Update .gitignore
```
# Add to your .gitignore
appsettings.*.json
appsettings.*.local.json
secrets.json
.user-secrets/
```

#### Step 4: For Production - Use Environment Variables
```bash
# Docker example
ENV BINANCE_APIKEY="production-key"
ENV BINANCE_APISECRET="production-secret"
```

---

## 🟠 PRIORITY 3: Enforce Daily Loss Limit

### Current Problem:
```csharp
// Function exists but is NEVER CALLED
public bool IsDailyLossExceeded(decimal currentBalance, decimal startingBalanceToday)
{
    var lossPercent = (startingBalanceToday - currentBalance) / startingBalanceToday;
    return lossPercent >= DailyLossLimitPercent;
}
```

### Solution:

#### Step 1: Add Method to Get Daily Starting Balance
```csharp
// Application\RiskManagementService.cs
public class RiskManagementService
{
    private readonly TradingBotDbContext _db;
    private const decimal MaxRiskPerTradePercent = 0.02m;
    private const decimal DailyLossLimitPercent = 0.05m;
    private const int MaxTradesPerDay = 5;
    private const int CircuitBreakerLossCount = 3;

    public RiskManagementService(TradingBotDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Get the starting balance for today (first portfolio snapshot of the day)
    /// </summary>
    public async Task<decimal> GetDailyStartingBalanceAsync()
    {
        var today = DateTime.UtcNow.Date;
        
        var snapshot = await _db.PortfolioSnapshots
            .Where(p => p.CreatedAt.Date == today)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        
        if (snapshot == null)
        {
            // No snapshot for today yet - this is the first trade
            // Return current balance (no loss yet)
            return 10000m;  // You should get real balance here
        }
        
        return snapshot.TotalBalanceUSDT;
    }

    // ... rest of methods ...
}
```

#### Step 2: Call Daily Loss Check in OpenTradeAsync
```csharp
// TradingBot.Infrastructure\Binance\BinanceTradeExecutionService.cs
public async Task<Order> OpenTradeAsync(TradeSignal signal)
{
    // 🔒 1️⃣ Max trades per day
    if (!_risk.CanTradeToday())
        throw new Exception("Max trades per day reached.");

    // 🔒 2️⃣ Circuit breaker
    if (_risk.IsCircuitBreakerTriggered())
        throw new Exception("Circuit breaker triggered. Too many losing trades.");

    // 🔒 3️⃣ Stop loss validation
    if (!_risk.IsStopLossValid(signal.EntryPrice, signal.StopLoss))
        throw new Exception("Invalid Stop Loss.");

    // 🔒 4️⃣ ✅ NEW: Daily loss limit check
    var accountBalance = await _accountService.GetAssetBalanceAsync("USDT");
    var startingBalance = await _risk.GetDailyStartingBalanceAsync();
    
    if (_risk.IsDailyLossExceeded(accountBalance, startingBalance))
        throw new Exception("Daily loss limit exceeded. Trading halted.");

    // 🔒 5️⃣ Position sizing
    var calculatedQuantity = _risk.CalculatePositionSize(
        accountBalance,
        signal.EntryPrice,
        signal.StopLoss);

    signal.Quantity = calculatedQuantity;
    
    // ... rest of trade opening ...
}
```

#### Step 3: Create Daily Snapshot on App Start
```csharp
// Program.cs - Add this after DbContext setup
#region Initialize Daily Baseline

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    var portfolioManager = scope.ServiceProvider.GetRequiredService<PortfolioManager>();
    
    // Create baseline snapshot if not exists for today
    var today = DateTime.UtcNow.Date;
    var existsToday = await db.PortfolioSnapshots
        .AnyAsync(p => p.CreatedAt.Date == today);
    
    if (!existsToday)
    {
        try
        {
            await portfolioManager.CreateSnapshotAsync();
        }
        catch (Exception ex)
        {
            // Log but don't fail startup
        }
    }
}

#endregion

app.Run();
```

---

## 🟠 PRIORITY 4: Implement Stop Loss / Take Profit Auto-Close

### Solution:

#### Step 1: Create Trade Monitoring Service
```csharp
// TradingBot.Application\Services\TradeMonitoringService.cs
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces;
using TradingBot.Persistence;

namespace TradingBot.Application.Services
{
    public interface ITradeMonitoringService
    {
        Task MonitorAndCloseTradesAsync();
    }

    public class TradeMonitoringService : ITradeMonitoringService
    {
        private readonly TradingBotDbContext _db;
        private readonly IMarketDataService _market;
        private readonly ITradeExecutionService _executor;
        private readonly ILogger<TradeMonitoringService> _logger;

        public TradeMonitoringService(
            TradingBotDbContext db,
            IMarketDataService market,
            ITradeExecutionService executor,
            ILogger<TradeMonitoringService> logger)
        {
            _db = db;
            _market = market;
            _executor = executor;
            _logger = logger;
        }

        public async Task MonitorAndCloseTradesAsync()
        {
            try
            {
                // Get all open trades
                var openTrades = await _db.Trades
                    .Where(t => t.Status == TradeStatus.Open)
                    .ToListAsync();

                if (openTrades.Count == 0)
                    return;

                _logger.LogInformation($"Monitoring {openTrades.Count} open trades...");

                foreach (var trade in openTrades)
                {
                    try
                    {
                        // Get current price
                        var currentPrice = await _market.GetCurrentPriceAsync(trade.Symbol);

                        // Check Take Profit
                        if (currentPrice >= trade.TakeProfit)
                        {
                            _logger.LogInformation($"TP hit for {trade.Symbol} at {currentPrice}. Closing trade.");
                            
                            await _executor.CloseTradeAsync(trade.ID);
                            
                            // Log the event
                            await LogTradeEventAsync(trade.ID, "TP_HIT", currentPrice);
                            continue;
                        }

                        // Check Stop Loss
                        if (currentPrice <= trade.StopLoss)
                        {
                            _logger.LogWarning($"SL hit for {trade.Symbol} at {currentPrice}. Closing trade.");
                            
                            await _executor.CloseTradeAsync(trade.ID);
                            
                            // Log the event
                            await LogTradeEventAsync(trade.ID, "SL_HIT", currentPrice);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error monitoring trade {trade.ID}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in trade monitoring service");
            }
        }

        private async Task LogTradeEventAsync(Guid tradeId, string eventType, decimal triggerPrice)
        {
            // Store event in SystemLog for audit trail
            var log = new SystemLog
            {
                Level = "INFO",
                Message = $"Trade {tradeId} closed by {eventType} at price {triggerPrice}"
            };

            _db.SystemLogs.Add(log);
            await _db.SaveChangesAsync();
        }
    }
}
```

#### Step 2: Create Background Worker
```csharp
// TradingBot\Workers\TradeMonitoringWorker.cs
using TradingBot.Application.Services;

namespace TradingBot.Workers
{
    public class TradeMonitoringWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TradeMonitoringWorker> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(10);  // Run every 10 seconds

        public TradeMonitoringWorker(IServiceProvider serviceProvider, ILogger<TradeMonitoringWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Trade Monitoring Worker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var monitor = scope.ServiceProvider.GetRequiredService<ITradeMonitoringService>();
                        await monitor.MonitorAndCloseTradesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in TradeMonitoringWorker");
                }

                await Task.Delay(_interval, stoppingToken);
            }

            _logger.LogInformation("Trade Monitoring Worker stopped");
        }
    }
}
```

#### Step 3: Register in Program.cs
```csharp
// TradingBot\Program.cs - Add to DI container
#region Background Services

builder.Services.AddScoped<ITradeMonitoringService, TradeMonitoringService>();
builder.Services.AddHostedService<TradeMonitoringWorker>();

#endregion
```

---

## 🟡 PRIORITY 5: Move Risk Parameters to Database

### Solution:

#### Step 1: Seed RiskProfile on App Start
```csharp
// TradingBot.Persistence\SeedData\RiskProfileSeeder.cs
using TradingBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace TradingBot.Persistence.SeedData
{
    public static class RiskProfileSeeder
    {
        public static async Task SeedDefaultRiskProfileAsync(TradingBotDbContext context)
        {
            // Check if default risk profile exists
            var existing = await context.RiskProfiles.FirstOrDefaultAsync();
            
            if (existing != null)
                return;  // Already seeded

            var defaultProfile = new RiskProfile
            {
                MaxRiskPerTradePercent = 0.02m,      // 2%
                MaxDailyLossPercent = 0.05m,         // 5%
                MaxTradesPerDay = 5,
                CircuitBreakerLossCount = 3,
                IsEnabled = true
            };

            context.RiskProfiles.Add(defaultProfile);
            await context.SaveChangesAsync();
        }
    }
}
```

#### Step 2: Update RiskManagementService to Load from DB
```csharp
// Application\RiskManagementService.cs
public class RiskManagementService
{
    private readonly TradingBotDbContext _db;
    private RiskProfile? _profile;

    public RiskManagementService(TradingBotDbContext db)
    {
        _db = db;
    }

    private async Task<RiskProfile> GetProfileAsync()
    {
        if (_profile == null)
        {
            _profile = await _db.RiskProfiles.FirstOrDefaultAsync()
                ?? throw new Exception("RiskProfile not configured in database");
        }
        return _profile;
    }

    public async Task<bool> CanTradeToday()
    {
        var profile = await GetProfileAsync();
        var today = DateTime.UtcNow.Date;

        var tradeCount = _db.Trades
            .Count(t => t.EntryTime.Date == today);

        return tradeCount < profile.MaxTradesPerDay;
    }

    public async Task<bool> IsCircuitBreakerTriggered()
    {
        var profile = await GetProfileAsync();
        var today = DateTime.UtcNow.Date;

        var losingTrades = _db.Trades
            .Where(o => o.CreatedAt.Date == today &&
                        o.Status == TradeStatus.Closed &&
                        o.PnL < 0)
            .Count();

        return losingTrades >= profile.CircuitBreakerLossCount;
    }

    public async Task<bool> IsDailyLossExceeded(decimal currentBalance, decimal startingBalanceToday)
    {
        var profile = await GetProfileAsync();
        var lossPercent = (startingBalanceToday - currentBalance) / startingBalanceToday;
        return lossPercent >= profile.MaxDailyLossPercent;
    }

    public async Task<decimal> CalculatePositionSize(
        decimal accountBalance,
        decimal entryPrice,
        decimal stopLoss)
    {
        var profile = await GetProfileAsync();
        
        if (!IsStopLossValid(entryPrice, stopLoss))
            throw new Exception("Invalid Stop Loss.");

        var riskAmount = accountBalance * profile.MaxRiskPerTradePercent;
        var riskPerUnit = entryPrice - stopLoss;

        if (riskPerUnit <= 0)
            throw new Exception("Invalid risk per unit.");

        var quantity = riskAmount / riskPerUnit;
        return Math.Round(quantity, 6);
    }

    public bool IsStopLossValid(decimal entryPrice, decimal stopLoss)
    {
        return stopLoss < entryPrice;  // spot long only
    }
}
```

#### Step 3: Add API Endpoint for Risk Management
```csharp
// TradingBot\Controllers\RiskController.cs
using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Entities;
using TradingBot.Persistence;

namespace TradingBot.API.Controllers
{
    [ApiController]
    [Route("api/risk")]
    public class RiskController(TradingBotDbContext db) : ControllerBase
    {
        private readonly TradingBotDbContext _db = db;

        [HttpGet("profile")]
        public async Task<IActionResult> GetRiskProfile()
        {
            var profile = await _db.RiskProfiles.FirstOrDefaultAsync();
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
```

#### Step 4: Call Seeder in Program.cs
```csharp
// TradingBot\Program.cs - Add after app.Build()
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    await RiskProfileSeeder.SeedDefaultRiskProfileAsync(db);
}
```

---

## ✅ IMPLEMENTATION CHECKLIST

### This Week:
- [ ] Fix BaseEntity ID to Guid
- [ ] Move credentials to user-secrets
- [ ] Implement daily loss limit enforcement
- [ ] Add background worker for SL/TP monitoring
- [ ] Move risk parameters to database

### Next Week:
- [ ] Add remaining API endpoints (GET operations)
- [ ] Implement logging/audit trail
- [ ] Add error handling & retries
- [ ] Create simple indicator computation service

### Following Week:
- [ ] Implement strategy engine
- [ ] Add Gemini AI integration
- [ ] Build signal generation

---

## 🧪 TESTING COMMANDS

### Test Trade Opening:
```bash
curl -X POST http://localhost:5000/api/trade/open \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "action": 1,
    "entryPrice": 43250,
    "stopLoss": 42900,
    "takeProfit": 43600,
    "quantity": 0.001,
    "aiConfidence": 85
  }'
```

### Test Risk Profile:
```bash
curl -X GET http://localhost:5000/api/risk/profile
```

### Update Risk Profile:
```bash
curl -X PUT http://localhost:5000/api/risk/profile \
  -H "Content-Type: application/json" \
  -d '{
    "maxRiskPerTradePercent": 0.025,
    "maxDailyLossPercent": 0.05,
    "maxTradesPerDay": 6,
    "circuitBreakerLossCount": 3,
    "isEnabled": true
  }'
```

---

## 📝 NOTES

- All changes are **backward compatible** with existing trades
- Database migration required after ID type change
- Test on testnet thoroughly before moving to live keys
- Monitor logs for any SL/TP triggering issues

