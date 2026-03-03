using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TradingBot.Application;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.AI;
using TradingBot.Infrastructure.Binance;
using TradingBot.Infrastructure.Binance.Models;
using TradingBot.Infrastructure.Services;
using TradingBot.Persistence;
using TradingBot.Persistence.SeedData;
using TradingBot.Services;
using TradingBot.Workers;

var builder = WebApplication.CreateBuilder(args);

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<TradingBotDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Binance ───────────────────────────────────────────────────────────────────
builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddHttpClient<ITradeExecutionService, BinanceTradeExecutionService>();
builder.Services.AddHttpClient<IMarketDataService, BinanceMarketDataService>();
builder.Services.AddHttpClient<BinanceAccountService>();

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<PortfolioManager>();
builder.Services.AddScoped<RiskManagementService>();
builder.Services.AddScoped<IRiskManagementService>(sp => sp.GetRequiredService<RiskManagementService>());

// Phase 1 — SL/TP auto-close background worker
builder.Services.AddScoped<ITradeMonitoringService, TradeMonitoringService>();
builder.Services.AddHostedService<TradeMonitoringWorker>();

// Phase 2 — Indicator + scanner + strategy engine
builder.Services.AddScoped<IIndicatorService, IndicatorCalculationService>();
builder.Services.AddScoped<IMarketScannerService, MarketScannerService>();
builder.Services.AddScoped<IStrategyEngine, StrategyEngine>();

// ── Phase 3 — AI Intelligence Layer ──────────────────────────────────────────
// Bind AI configuration (API keys come from user-secrets, models from appsettings.json)
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));

// Individual AI provider HTTP clients (each gets its own named client for clean isolation)
builder.Services.AddHttpClient<GeminiAIService>();
builder.Services.AddHttpClient<GroqAIService>();
builder.Services.AddHttpClient<OpenRouterAIService>();
builder.Services.AddHttpClient<CohereAIService>();

// Register individual providers as scoped services
builder.Services.AddScoped<GeminiAIService>();
builder.Services.AddScoped<GroqAIService>();
builder.Services.AddScoped<OpenRouterAIService>();
builder.Services.AddScoped<CohereAIService>();

// Multi-provider orchestrator — this is what the rest of the app uses as IAISignalService
builder.Services.AddScoped<MultiProviderAIService>();
builder.Services.AddScoped<IAISignalService>(sp => sp.GetRequiredService<MultiProviderAIService>());

// AI-enhanced pipeline services
builder.Services.AddScoped<MarketRegimeDetector>();
builder.Services.AddScoped<AIEnhancedStrategyEngine>();

// Phase 2+3 — Signal generation worker (AI-enhanced)
builder.Services.AddHostedService<SignalGenerationWorker>();

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TradingBot API",
        Version = "v1",
        Description = "Automated crypto trading bot — Phase 3: Multi-Model AI Intelligence"
    });
});

var app = builder.Build();

// ── Startup Initialization ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    var pfMgr = scope.ServiceProvider.GetRequiredService<PortfolioManager>();

    // Apply any pending EF Core migrations automatically
    await db.Database.MigrateAsync();

    // Seed defaults: RiskProfile + active TradingPairs
    await RiskProfileSeeder.SeedDefaultRiskProfileAsync(db);
    await TradingPairsSeeder.SeedDefaultPairsAsync(db);

    // Create today's portfolio baseline snapshot (used by daily loss limit check)
    var today = DateTime.UtcNow.Date;
    var hasSnapshotToday = await db.PortfolioSnapshots!
        .AnyAsync(p => p.CreatedAt.Date == today);

    if (!hasSnapshotToday)
    {
        try
        {
            await pfMgr.CreateSnapshotAsync();
            app.Logger.LogInformation("Portfolio baseline snapshot created for {Date:yyyy-MM-dd}", today);
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning("Could not create portfolio snapshot on startup: {Msg}", ex.Message);
        }
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();