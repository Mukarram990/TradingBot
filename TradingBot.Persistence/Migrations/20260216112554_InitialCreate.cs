using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AIResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawResponse = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParsedAction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Confidence = table.Column<int>(type: "int", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AIResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Candles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Interval = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OpenTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CloseTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Open = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    High = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Low = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Close = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Volume = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Candles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyPerformances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalTrades = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    Losses = table.Column<int>(type: "int", nullable: false),
                    NetPnL = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    MaxDrawdown = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    WinRate = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyPerformances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndicatorSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RSI = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EMA20 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    EMA50 = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    MACD = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ATR = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    VolumeSpike = table.Column<bool>(type: "bit", nullable: false),
                    Trend = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SupportLevel = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ResistanceLevel = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicatorSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketRegimes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Trend = table.Column<int>(type: "int", nullable: false),
                    Volatility = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketRegimes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BinanceOrderId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderType = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalBalanceUSDT = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    TotalUnrealizedPnL = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    TotalOpenPositions = table.Column<int>(type: "int", nullable: false),
                    DailyPnL = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    AverageEntry = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    UnrealizedPnL = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxRiskPerTradePercent = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    MaxDailyLossPercent = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    MaxTradesPerDay = table.Column<int>(type: "int", nullable: false),
                    CircuitBreakerLossCount = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Strategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MinConfidenceRequired = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Strategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StackTrace = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    ExitPrice = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    StopLoss = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    TakeProfit = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    PnL = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    PnLPercentage = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EntryTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExitTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AIConfidence = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradeSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Action = table.Column<int>(type: "int", nullable: false),
                    Confidence = table.Column<int>(type: "int", nullable: false),
                    SuggestedStopLoss = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    SuggestedTakeProfit = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    IsExecuted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeSignals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TradingPairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BaseAsset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuoteAsset = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MinQty = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    StepSize = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingPairs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalBalance = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    LockedBalance = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Candles_Symbol_OpenTime",
                table: "Candles",
                columns: new[] { "Symbol", "OpenTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyPerformances_Date",
                table: "DailyPerformances",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IndicatorSnapshots_Symbol_Timestamp",
                table: "IndicatorSnapshots",
                columns: new[] { "Symbol", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Symbol",
                table: "Trades",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "IX_TradeSignals_Symbol_CreatedAt",
                table: "TradeSignals",
                columns: new[] { "Symbol", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TradingPairs_Symbol",
                table: "TradingPairs",
                column: "Symbol",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AIResponses");

            migrationBuilder.DropTable(
                name: "Candles");

            migrationBuilder.DropTable(
                name: "DailyPerformances");

            migrationBuilder.DropTable(
                name: "IndicatorSnapshots");

            migrationBuilder.DropTable(
                name: "MarketRegimes");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PortfolioSnapshots");

            migrationBuilder.DropTable(
                name: "Positions");

            migrationBuilder.DropTable(
                name: "RiskProfiles");

            migrationBuilder.DropTable(
                name: "Strategies");

            migrationBuilder.DropTable(
                name: "SystemLogs");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "TradeSignals");

            migrationBuilder.DropTable(
                name: "TradingPairs");

            migrationBuilder.DropTable(
                name: "UserAccounts");
        }
    }
}
