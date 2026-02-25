using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TradingBot.Application;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance;
using TradingBot.Infrastructure.Binance.Models;
using TradingBot.Infrastructure.Services;
using TradingBot.Persistence;
using TradingBot.Persistence.SeedData;
using TradingBot.Services;
using TradingBot.Workers;

var builder = WebApplication.CreateBuilder(args);

#region Database

builder.Services.AddDbContext<TradingBotDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

#endregion

#region Binance Configuration

builder.Services.Configure<BinanceOptions>(
    builder.Configuration.GetSection("Binance"));

builder.Services.AddHttpClient<ITradeExecutionService, BinanceTradeExecutionService>();
builder.Services.AddHttpClient<IMarketDataService, BinanceMarketDataService>();
builder.Services.AddHttpClient<BinanceAccountService>();

#endregion

#region Application Services

builder.Services.AddScoped<PortfolioManager>();
builder.Services.AddScoped<RiskManagementService>();
builder.Services.AddScoped<IRiskManagementService>(sp => sp.GetRequiredService<RiskManagementService>());

// Phase 1: Trade monitoring background worker
builder.Services.AddScoped<ITradeMonitoringService, TradeMonitoringService>();
builder.Services.AddHostedService<TradeMonitoringWorker>();

// Phase 2 — Step 1: Indicator calculation
builder.Services.AddScoped<IIndicatorService, IndicatorCalculationService>();

// Phase 2 — Step 2: Market scanner (depends on IIndicatorService)
builder.Services.AddScoped<IMarketScannerService, MarketScannerService>();

// Phase 2 — Step 3: Strategy engine (depends on IMarketScannerService)
builder.Services.AddScoped<IStrategyEngine, StrategyEngine>();

#endregion

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TradingBot API",
        Version = "v1",
        Description = "Automated crypto trading bot — Binance integration"
    });
});

var app = builder.Build();

#region Startup Initialization

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    var portfolioManager = scope.ServiceProvider.GetRequiredService<PortfolioManager>();

    // 1. Apply any pending EF migrations automatically
    await db.Database.MigrateAsync();

    // 2. Seed default risk profile (2% risk, 5% daily loss limit, 5 trades/day)
    await RiskProfileSeeder.SeedDefaultRiskProfileAsync(db);

    // 3. Seed default trading pairs (BTC, ETH, BNB, SOL, XRP)
    await TradingPairsSeeder.SeedDefaultPairsAsync(db);

    // 4. Create today's portfolio baseline snapshot if not already done
    var today = DateTime.UtcNow.Date;
    bool hasToday = await db.PortfolioSnapshots!
        .AnyAsync(p => p.CreatedAt.Date == today);

    if (!hasToday)
    {
        try
        {
            await portfolioManager.CreateSnapshotAsync();
            app.Logger.LogInformation(
                "Portfolio baseline snapshot created for {Date:yyyy-MM-dd}", today);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(
                "Could not create portfolio snapshot at startup: {Message}", ex.Message);
        }
    }
}

#endregion

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();