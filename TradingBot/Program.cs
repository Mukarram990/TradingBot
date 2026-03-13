using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TradingBot.API.Middleware;
using TradingBot.API.Services;
using TradingBot.API.Workers;
using TradingBot.Application;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure;
using TradingBot.Infrastructure.AI;
using TradingBot.Infrastructure.Binance;
using TradingBot.Infrastructure.Binance.Models;
using TradingBot.Infrastructure.Services;
using TradingBot.Persistence;
using TradingBot.Persistence.SeedData;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();

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
var strategyOptions = builder.Configuration.GetSection("Strategy")
    .Get<TradingBot.Infrastructure.Services.StrategyOptions>()
    ?? new TradingBot.Infrastructure.Services.StrategyOptions();
builder.Services.AddSingleton(strategyOptions);
builder.Services.Configure<TradingBot.Infrastructure.Services.TradingOptions>(
    builder.Configuration.GetSection("Trading"));
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

// ── CORS (Enable frontend dashboard) ──────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
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

    // Swagger documentation for API Key auth
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-API-KEY",
        Description = "API Key authentication. Header: X-API-KEY: {key}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

app.MapPost("/api/strategy/mode", (StrategyModeRequest req, TradingBot.Infrastructure.Services.StrategyOptions opts) =>
{
    var mode = (req.Mode ?? "Strict").Trim();
    opts.StrategyMode = mode;
    if (req.RsiOversold.HasValue) opts.RsiOversold = req.RsiOversold.Value;
    if (req.RsiOverbought.HasValue) opts.RsiOverbought = req.RsiOverbought.Value;
    if (req.RelaxedRsiMax.HasValue) opts.RelaxedRsiMax = req.RelaxedRsiMax.Value;
    if (req.MomentumRsiMin.HasValue) opts.MomentumRsiMin = req.MomentumRsiMin.Value;
    if (req.MomentumRsiMax.HasValue) opts.MomentumRsiMax = req.MomentumRsiMax.Value;
    if (req.RequireVolumeSpikeForMomentum.HasValue) opts.RequireVolumeSpikeForMomentum = req.RequireVolumeSpikeForMomentum.Value;
    return Results.Ok(new { mode = opts.StrategyMode });
}).RequireAuthorization();

app.MapGet("/api/strategy/mode", (TradingBot.Infrastructure.Services.StrategyOptions opts) =>
{
    return Results.Ok(new
    {
        mode = opts.StrategyMode,
        rsiOversold = opts.RsiOversold,
        rsiOverbought = opts.RsiOverbought,
        relaxedRsiMax = opts.RelaxedRsiMax,
        momentumRsiMin = opts.MomentumRsiMin,
        momentumRsiMax = opts.MomentumRsiMax,
        requireVolumeSpikeForMomentum = opts.RequireVolumeSpikeForMomentum
    });
}).RequireAuthorization();

app.MapPost("/api/strategy/custom", async (StrategyCreateRequest req, TradingBotDbContext db) =>
{
    var name = string.IsNullOrWhiteSpace(req.Name) ? "Custom Strategy" : req.Name.Trim();
    var def = new TradingBot.Infrastructure.Services.StrategyDefinition
    {
        Type = string.IsNullOrWhiteSpace(req.Type) ? "ema_crossover" : req.Type.Trim().ToLowerInvariant(),
        Weight = req.Weight is > 0 ? req.Weight.Value : 1.0m,
        FastEma = req.FastEma is > 0 ? req.FastEma.Value : 20,
        SlowEma = req.SlowEma is > 0 ? req.SlowEma.Value : 50,
        UseRsi = req.UseRsi ?? true,
        RsiMin = req.RsiMin ?? 45m,
        RsiMax = req.RsiMax ?? 70m,
        UseMacd = req.UseMacd ?? true,
        MacdMin = req.MacdMin ?? 0m,
        UseAtr = req.UseAtr ?? false,
        AtrMin = req.AtrMin ?? 0m,
        RequireVolumeSpike = req.RequireVolumeSpike ?? false,
        MinConfidence = req.MinConfidence is > 0 ? req.MinConfidence.Value : 70
    };

    if (def.FastEma != 20 || def.SlowEma != 50)
        return Results.BadRequest("Only EMA20/EMA50 are currently supported.");

    var strategy = new TradingBot.Domain.Entities.Strategy
    {
        Name = name,
        Version = def.Type,
        Description = System.Text.Json.JsonSerializer.Serialize(def),
        IsActive = req.Activate ?? false,
        MinConfidenceRequired = def.MinConfidence
    };

    db.Strategies!.Add(strategy);
    await db.SaveChangesAsync();

    return Results.Ok(new { id = strategy.ID, name = strategy.Name, active = strategy.IsActive });
}).RequireAuthorization();

app.MapGet("/api/strategy/custom", async (TradingBotDbContext db) =>
{
    var list = await db.Strategies!
        .AsNoTracking()
        .OrderByDescending(s => s.CreatedAt)
        .Select(s => new { s.ID, s.Name, s.IsActive, s.Version, s.MinConfidenceRequired, s.Description, s.CreatedAt })
        .ToListAsync();
    return Results.Ok(new { data = list });
}).RequireAuthorization();

app.MapPost("/api/strategy/custom/{id:int}/activate", async (int id, TradingBotDbContext db) =>
{
    var strategy = await db.Strategies!.FirstOrDefaultAsync(s => s.ID == id);
    if (strategy == null) return Results.NotFound();

    strategy.IsActive = true;
    strategy.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { id = strategy.ID, active = true });
}).RequireAuthorization();

app.MapPost("/api/strategy/custom/{id:int}/deactivate", async (int id, TradingBotDbContext db) =>
{
    var strategy = await db.Strategies!.FirstOrDefaultAsync(s => s.ID == id);
    if (strategy == null) return Results.NotFound();

    strategy.IsActive = false;
    strategy.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { id = strategy.ID, active = false });
}).RequireAuthorization();

// ── HTTPS Redirect (Production Security) ──────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// ── Phase 4 middleware (before routing) ───────────────────────────────────────
// Clean JSON error responses + auto-logs every exception to SystemLogs table
app.UseExceptionHandler(GlobalExceptionHandler.Handle);

// CORS
app.UseCors("AllowAll");

// API Key Authentication middleware
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

// ── Startup Initialization ────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TradingBotDbContext>();
    var pfMgr = scope.ServiceProvider.GetRequiredService<PortfolioManager>();

    await db.Database.MigrateAsync();

    await RiskProfileSeeder.SeedDefaultRiskProfileAsync(db);
    await TradingPairsSeeder.SeedDefaultPairsAsync(db);
    await DefaultAdminSeeder.SeedAsync(db, builder.Configuration, app.Logger);
    await StrategySeeder.SeedDefaultStrategyAsync(db);

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

record StrategyModeRequest(
    string? Mode,
    decimal? RsiOversold,
    decimal? RsiOverbought,
    decimal? RelaxedRsiMax,
    decimal? MomentumRsiMin,
    decimal? MomentumRsiMax,
    bool? RequireVolumeSpikeForMomentum);

record StrategyCreateRequest(
    string? Name,
    string? Type,
    decimal? Weight,
    int? FastEma,
    int? SlowEma,
    bool? UseRsi,
    decimal? RsiMin,
    decimal? RsiMax,
    bool? UseMacd,
    decimal? MacdMin,
    bool? UseAtr,
    decimal? AtrMin,
    bool? RequireVolumeSpike,
    int? MinConfidence,
    bool? Activate);
