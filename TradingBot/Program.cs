using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
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
builder.Services.AddScoped<PortfolioManager>();
builder.Services.AddScoped<RiskManagementService>();
builder.Services.AddScoped<IRiskManagementService>(sp => sp.GetRequiredService<RiskManagementService>());

// ✅ NEW: Register Trade Monitoring Service (PRIORITY 3 FIX)
builder.Services.AddScoped<ITradeMonitoringService, TradeMonitoringService>();
builder.Services.AddHostedService<TradeMonitoringWorker>();

#endregion

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TradingBot API",
        Version = "v1",
        Description = "API for TradingBot services"
    });
});

var app = builder.Build();

#region Initialize Data

// ✅ NEW: Initialize daily baseline snapshot (PRIORITY 2 FIX)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    var portfolioManager = scope.ServiceProvider.GetRequiredService<PortfolioManager>();

    // Apply migrations
    await db.Database.MigrateAsync();

    // Seed risk profile
    await RiskProfileSeeder.SeedDefaultRiskProfileAsync(db);

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
            app.Logger.LogWarning($"Could not create initial portfolio snapshot: {ex.Message}");
        }
    }
}

#endregion

#region Middleware

// Allow enabling Swagger in Development or via configuration key "Swagger:EnableInProduction"
var enableSwagger = app.Environment.IsDevelopment() ||
                    builder.Configuration.GetValue<bool>("Swagger:EnableInProduction");

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TradingBot API v1");
        c.RoutePrefix = string.Empty; // Serve UI at app root
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

#endregion

app.Run();
