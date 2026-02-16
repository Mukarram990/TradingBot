using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using TradingBot.Domain.Interfaces;
using TradingBot.Infrastructure.Binance;
using TradingBot.Persistence;

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
