# 🤖 TradingBot

> An automated crypto trading bot built on **.NET 10** + **C#**, integrating with the **Binance API** for real-time trade execution, risk management, and portfolio protection — designed to eventually run autonomously with AI-powered signal validation.

---

## 📋 Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [API Reference](#api-reference)
- [Configuration](#configuration)
- [Roadmap](#roadmap)
- [Development Progress](#development-progress)

---

## Overview

TradingBot is a server-side trading automation system that connects to the Binance testnet (and eventually mainnet) to execute spot trades. It enforces strict risk rules, monitors open positions 24/7, and is being built toward fully autonomous signal-to-trade execution powered by technical indicators and Gemini AI.

**Current Phase**: Phase 1 complete — secure foundation with full trade lifecycle, automated SL/TP monitoring, and configurable risk management.

---

## Architecture

The solution follows a clean **layered architecture** with full separation of concerns:

```
┌──────────────────────────────────────────────────────────────┐
│                    TradingBot (API Layer)                     │
│   Controllers · Workers · Services · Program.cs              │
└────────────────────────────┬─────────────────────────────────┘
                             │
              ┌──────────────┼──────────────┐
              ▼              ▼              ▼
┌─────────────────┐  ┌──────────────┐  ┌──────────────────────┐
│ TradingBot      │  │ TradingBot   │  │ TradingBot           │
│ .Domain         │  │ .Persistence │  │ .Infrastructure      │
│                 │  │              │  │                      │
│ Entities        │  │ DbContext    │  │ Binance API clients  │
│ Interfaces      │  │ Migrations   │  │ Signature service    │
│ Enums           │  │ Seed data    │  │ Trade monitoring     │
└─────────────────┘  └──────────────┘  └──────────────────────┘
              │
              ▼
┌─────────────────┐
│ TradingBot      │
│ .Application    │
│                 │
│ Indicator calc  │
│ Market scanner  │
│ Strategy engine │
└─────────────────┘
```

---

## Features

### ✅ Implemented (Phase 1)

| Feature | Description |
|---|---|
| **Trade Execution** | Open and close spot trades on Binance via REST API |
| **Position Sizing** | Automatic 2% risk-per-trade calculation (Kelly Criterion) |
| **Daily Loss Limit** | Stops all trading if portfolio drops >5% in a day |
| **Circuit Breaker** | Halts trading after N consecutive losing trades |
| **SL/TP Auto-Close** | Background worker checks open trades every 10 seconds and closes on trigger |
| **Risk Profile API** | Change risk parameters at runtime via API — no recompile needed |
| **Portfolio Snapshots** | Daily baseline balance tracking for loss calculation |
| **Secure Config** | API keys via .NET User Secrets (never in source code) |
| **Auto DB Migration** | Database migrates automatically on app startup |

### 🔜 In Progress (Phase 2)

| Feature | Description |
|---|---|
| **Indicator Engine** | RSI, EMA20/50, MACD, ATR, Volume Spike calculation |
| **Market Scanner** | Fetch and filter trading pairs with candle data |
| **Strategy Engine** | Generate buy/sell signals from indicator combinations |
| **Signal Worker** | Background job auto-converts signals to trades |

### 📅 Planned (Phase 3+)

| Feature | Description |
|---|---|
| **Gemini AI** | AI validates signals before trade execution |
| **Market Regime** | Detect bull/bear/sideways market conditions |
| **Performance Analytics** | Win rate, Sharpe ratio, daily PnL tracking |
| **Multi-pair Scanning** | Scan multiple trading pairs simultaneously |

---

## Tech Stack

| Layer | Technology |
|---|---|
| **Runtime** | .NET 10, C# |
| **Database** | SQL Server (MSSQL) via Entity Framework Core 10 |
| **Exchange** | Binance REST API (Testnet + Mainnet) |
| **API** | ASP.NET Core Web API with Swagger |
| **Background Jobs** | .NET Hosted Services (`BackgroundService`) |
| **Secrets** | .NET User Secrets (local) / Environment Variables (production) |

---

## Project Structure

```
TradingBot/                          ← Main API project
│
├── Controllers/
│   ├── TradeController.cs           ← Open/close trades
│   ├── MarketController.cs          ← Price & candle data
│   ├── PortfolioController.cs       ← Portfolio snapshots
│   └── RiskController.cs            ← Risk profile management
│
├── Services/
│   ├── RiskManagementService.cs     ← Risk rules enforcement
│   └── PortfolioManager.cs          ← Balance snapshot logic
│
├── Workers/
│   └── TradeMonitoringWorker.cs     ← SL/TP background monitor
│
├── appsettings.json                 ← Config (NO secrets here)
└── Program.cs                       ← DI registration & startup

TradingBot.Domain/                   ← Core business models
├── Entities/
│   ├── Trade.cs
│   ├── Order.cs
│   ├── TradeSignal.cs
│   ├── PortfolioSnapshot.cs
│   ├── RiskProfile.cs
│   ├── IndicatorSnapshot.cs
│   └── ...
├── Interfaces/
│   ├── ITradeExecutionService.cs
│   ├── IMarketDataService.cs
│   ├── IRiskManagementService.cs
│   └── IIndicatorService.cs
└── Enums/
    ├── TradeStatus.cs
    └── TradeAction.cs

TradingBot.Infrastructure/           ← Binance integration
└── Binance/
    ├── BinanceTradeExecutionService.cs
    ├── BinanceMarketDataService.cs
    ├── BinanceAccountService.cs
    ├── BinanceSignatureService.cs
    └── Services/
        └── TradeMonitoringService.cs

TradingBot.Persistence/              ← Database layer
├── TradingBotDbContext.cs
├── Migrations/
└── SeedData/
    └── RiskProfileSeeder.cs

TradingBot.Application/              ← Business logic (Phase 2)
├── IndicatorCalculationService.cs   ← [in progress]
└── MarketScannerService.cs          ← [in progress]
```

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [SQL Server](https://www.microsoft.com/en-us/sql-server) (or SQL Server Express)
- [Binance Testnet Account](https://testnet.binance.vision/) — free, no real money
- [EF Core CLI tools](https://learn.microsoft.com/en-us/ef/core/cli/dotnet)

```bash
dotnet tool install --global dotnet-ef
```

---

### 1. Clone the repo

```bash
git clone https://github.com/Mukarram990/TradingBot.git
cd TradingBot
```

---

### 2. Configure the database connection

Open `TradingBot/appsettings.json` and update the connection string to match your SQL Server instance:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\MSSQLSERVER01;Database=TradingBotDb;Trusted_Connection=True;TrustServerCertificate=TRUE;"
  },
  "Binance": {
    "BaseUrl": "https://demo-api.binance.com"
  }
}
```

---

### 3. Set your Binance API keys (User Secrets)

**Never put API keys in `appsettings.json`.** Use .NET User Secrets instead:

```bash
cd TradingBot

dotnet user-secrets init

dotnet user-secrets set "Binance:ApiKey"    "your-testnet-api-key"
dotnet user-secrets set "Binance:ApiSecret" "your-testnet-api-secret"

# Verify
dotnet user-secrets list
```

> For production deployments, use environment variables:
> ```bash
> $env:Binance__ApiKey    = "your-key"
> $env:Binance__ApiSecret = "your-secret"
> ```

---

### 4. Run the application

```bash
cd ..   # back to solution root
dotnet run --project TradingBot
```

The app will automatically:
- Run all pending database migrations
- Seed a default `RiskProfile` (2% risk, 5% daily loss limit, 5 trades/day)
- Create today's portfolio baseline snapshot
- Start the SL/TP background monitoring worker

---

### 5. Open Swagger UI

Navigate to: **http://localhost:5000/swagger**

You'll see all available endpoints ready to test.

---

## API Reference

### Trade Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/trade/open` | Open a new trade on Binance |
| `POST` | `/api/trade/close/{tradeId}` | Close an open trade |

**Open Trade — Request Body:**
```json
{
  "symbol": "BTCUSDT",
  "action": 1,
  "entryPrice": 43250.50,
  "stopLoss": 42900.00,
  "takeProfit": 43800.00,
  "quantity": 0.001,
  "aiConfidence": 85
}
```

> `quantity` is automatically recalculated by the 2% position sizing rule. Your provided value is overridden.

---

### Market Data

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/market/price/{symbol}` | Get current price for a symbol |
| `GET` | `/api/market/candles?symbol=BTCUSDT&interval=1h&limit=100` | Get OHLCV candle data |

---

### Portfolio

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/portfolio/snapshot` | Create a portfolio balance snapshot |

---

### Risk Management

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/risk/profile` | View current risk settings |
| `PUT` | `/api/risk/profile` | Update risk settings at runtime |

**Update Risk Profile — Request Body:**
```json
{
  "maxRiskPerTradePercent": 0.02,
  "maxDailyLossPercent": 0.05,
  "maxTradesPerDay": 5,
  "circuitBreakerLossCount": 3,
  "isEnabled": true
}
```

---

## Configuration

### Risk Rules (all configurable via API)

| Setting | Default | Description |
|---------|---------|-------------|
| `MaxRiskPerTradePercent` | `0.02` (2%) | Max portfolio % risked per trade |
| `MaxDailyLossPercent` | `0.05` (5%) | Trading halts if daily loss exceeds this |
| `MaxTradesPerDay` | `5` | Max number of trades opened per day |
| `CircuitBreakerLossCount` | `3` | Trading halts after N consecutive losses |

### Background Worker

The `TradeMonitoringWorker` runs every **10 seconds** and:
1. Fetches all trades with `Status = Open`
2. Gets the current Binance price for each
3. Compares against `StopLoss` and `TakeProfit`
4. Auto-closes the trade and logs the event if triggered

---

## Roadmap

```
Phase 1: Foundation ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ✅ COMPLETE
  ✔ Binance API integration (trade, market, account)
  ✔ Full trade lifecycle (open → monitor → auto-close)
  ✔ Risk management (position sizing, daily limits, circuit breaker)
  ✔ Secure credential management
  ✔ DB auto-migration & seeding

Phase 2: Automation ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ 🔨 NEXT
  ☐ IndicatorCalculationService (RSI, EMA, MACD, ATR)
  ☐ MarketScannerService (fetch & filter pairs)
  ☐ StrategyEngine (generate TradeSignals from indicators)
  ☐ SignalWorker (auto-convert high-confidence signals → trades)
  ☐ IndicatorSnapshot persistence

Phase 3: Intelligence ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ 📅 PLANNED
  ☐ Gemini AI signal validation
  ☐ Market regime detection (bull / bear / sideways)
  ☐ Multi-pair scanning
  ☐ Performance analytics (win rate, Sharpe ratio, daily PnL)

Phase 4: Production Hardening ━━━━━━━━━━━━━━━━━━━━━━━━━━ 📅 PLANNED
  ☐ Retry policies & rate limit handling
  ☐ Health check endpoints
  ☐ Structured logging & audit trails
  ☐ Load testing & performance tuning
```

---

## Development Progress

| Component | Status | Notes |
|---|---|---|
| Binance Trade Execution | ✅ Done | MARKET orders, fills, rollback |
| Binance Market Data | ✅ Done | Price, OHLCV candles |
| Binance Account | ✅ Done | Balance fetching |
| Trade Lifecycle | ✅ Done | Open, monitor, close |
| Position Sizing | ✅ Done | 2% risk rule |
| Daily Loss Limit | ✅ Done | Portfolio snapshot baseline |
| SL/TP Auto-Close | ✅ Done | Background worker, 10s interval |
| Risk Profile API | ✅ Done | GET/PUT, DB-backed |
| DB Schema | ✅ Done | int IDENTITY, all tables |
| Secure Secrets | ✅ Done | User Secrets / env vars |
| Indicator Calculation | 🔨 Next | RSI, EMA, MACD, ATR |
| Market Scanner | 🔨 Next | Pair filtering, candle pull |
| Strategy Engine | 🔨 Next | Signal generation |
| Signal → Trade Worker | 🔨 Next | Auto-trading loop |
| Gemini AI Integration | 📅 Planned | Signal validation |
| Performance Analytics | 📅 Planned | Win rate, Sharpe ratio |

---

## Common Commands

```bash
# Build
dotnet build

# Run
dotnet run --project TradingBot

# Create migration
dotnet ef migrations add <MigrationName> -p TradingBot.Persistence -s TradingBot

# Apply migrations
dotnet ef database update -p TradingBot.Persistence -s TradingBot

# Drop database (for fresh start)
dotnet ef database drop -p TradingBot.Persistence -s TradingBot

# List migrations
dotnet ef migrations list -p TradingBot.Persistence -s TradingBot

# View secrets
cd TradingBot && dotnet user-secrets list
```

---

## Security Notes

- ❌ **Never commit API keys** — they belong in User Secrets or environment variables only
- ✅ `appsettings.json` contains only non-sensitive config (`BaseUrl`, connection string for local dev)
- ✅ All secrets are loaded at runtime via .NET's configuration pipeline
- ✅ `.gitignore` excludes `secrets.json` and `.env` files

---

*Built with .NET 10 · Binance API · SQL Server · Entity Framework Core*
