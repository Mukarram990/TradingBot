using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TradingBot.Application;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.AI;
using TradingBot.Infrastructure.Binance;
using TradingBot.Infrastructure.Binance.Models;
using TradingBot.Infrastructure.Resilience;
using TradingBot.Infrastructure.Services;
using TradingBot.Middleware;
using TradingBot.Persistence;
using TradingBot.Persistence.SeedData;
using TradingBot.Services;
using TradingBot.Workers;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<TradingBotDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Binance (Phase 4: retry + circuit breaker on every client) ────────────────
builder.Services.Configure<BinanceOptions>(builder.Configuration.GetSection("Binance"));
builder.Services.AddHttpClient<ITradeExecutionService, BinanceTradeExecutionService>()
    .AddBinanceResilience();
builder.Services.AddHttpClient<IMarketDataService, BinanceMarketDataService>()
    .AddBinanceResilience();
builder.Services.AddHttpClient<BinanceAccountService>()
    .AddBinanceResilience();

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<PortfolioManager>();
builder.Services.AddScoped<RiskManagementService>();
builder.Services.AddScoped<IRiskManagementService>(sp => sp.GetRequiredService<RiskManagementService>());

// Phase 1 - SL/TP auto-close background worker
builder.Services.AddScoped<ITradeMonitoringService, TradeMonitoringService>();
builder.Services.AddHostedService<TradeMonitoringWorker>();

// Phase 2 - Indicator + scanner + strategy engine (Infrastructure layer — needs DB access)
builder.Services.AddScoped<IIndicatorService, TradingBot.Infrastructure.Services.IndicatorCalculationService>();
builder.Services.AddScoped<IMarketScannerService, TradingBot.Infrastructure.Services.MarketScannerService>();
builder.Services.AddScoped<IStrategyEngine, TradingBot.Infrastructure.Services.StrategyEngine>();

// ── Phase 3 - AI Intelligence Layer ──────────────────────────────────────────
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));

// AI provider HTTP clients (Phase 4: network-error retry on each)
builder.Services.AddHttpClient<GeminiAIService>().AddAiProviderResilience();
builder.Services.AddHttpClient<GroqAIService>().AddAiProviderResilience();
builder.Services.AddHttpClient<OpenRouterAIService>().AddAiProviderResilience();
builder.Services.AddHttpClient<CohereAIService>().AddAiProviderResilience();

builder.Services.AddScoped<GeminiAIService>();
builder.Services.AddScoped<GroqAIService>();
builder.Services.AddScoped<OpenRouterAIService>();
builder.Services.AddScoped<CohereAIService>();

builder.Services.AddScoped<MultiProviderAIService>();
builder.Services.AddScoped<IAISignalService>(sp => sp.GetRequiredService<MultiProviderAIService>());

builder.Services.AddScoped<MarketRegimeDetector>();
builder.Services.AddScoped<AIEnhancedStrategyEngine>();

builder.Services.AddHostedService<SignalGenerationWorker>();

// ── Phase 4 - Production Hardening ───────────────────────────────────────────
// Auto-populate DailyPerformances table at midnight UTC every night
builder.Services.AddHostedService<DailyPerformanceWorker>();

// Rate limiting: 60 requests / 60 seconds per IP (returns HTTP 429 when exceeded)
builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("global", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromSeconds(60);
        limiter.SegmentsPerWindow = 6;
        limiter.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 10;
    });
    options.RejectionStatusCode = 429;
});

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "TradingBot API",
        Version = "v1",
        Description = "Automated crypto trading bot - Phase 4: Production Hardened"
    });
});

var app = builder.Build();

// ── Phase 4 middleware (before routing) ───────────────────────────────────────
// Clean JSON error responses + auto-logs every exception to SystemLogs table
app.UseExceptionHandler(GlobalExceptionHandler.Handle);

// Rate limiter
app.UseRateLimiter();

// ── Startup Initialization ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    var pfMgr = scope.ServiceProvider.GetRequiredService<PortfolioManager>();

    await db.Database.MigrateAsync();

    await RiskProfileSeeder.SeedDefaultRiskProfileAsync(db);
    await TradingPairsSeeder.SeedDefaultPairsAsync(db);

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

// Apply rate limiter to all controller routes
app.MapControllers().RequireRateLimiting("global");

app.Run();