# TradingBot — Automated Crypto Trading System

> **Stack:** .NET 10 · SQL Server · Binance API · Multi-AI (Gemini · Groq · OpenRouter · Cohere)  
> **Status:** ✅ Production-Hardened · All 4 phases complete · Security implemented  
> **Base URL (local):** `http://localhost:5000`  
> **Swagger UI:** `http://localhost:5000/swagger`

---

## Table of Contents

1. [What This System Does](#1-what-this-system-does)
2. [Architecture](#2-architecture)
3. [Project Structure](#3-project-structure)
4. [Prerequisites & Setup](#4-prerequisites--setup)
5. [Configuration & Secrets](#5-configuration--secrets)
6. [Running the Application](#6-running-the-application)
7. [Complete API Reference](#7-complete-api-reference)
8. [Response Shapes & Enums](#8-response-shapes--enums)
9. [Error Handling](#9-error-handling)
10. [Rate Limiting & Security](#10-rate-limiting--security)
11. [Background Workers](#11-background-workers)
12. [Database Schema Overview](#12-database-schema-overview)
13. [Frontend Integration Guide](#13-frontend-integration-guide)
14. [Development Notes](#14-development-notes)

---

## 1. What This System Does

TradingBot is a fully automated spot-crypto trading system that:

- **Scans** up to 5 configurable Binance pairs every 5 minutes
- **Calculates** 7 technical indicators (RSI, EMA20/50, MACD, ATR, Volume Spike, Support/Resistance)
- **Evaluates** a rule-based strategy engine with 5 confluence gates and confidence scoring
- **Validates** signals through a 4-provider AI layer (Gemini → Groq → OpenRouter → Cohere) with automatic failover on rate limits
- **Executes** trades on Binance using MARKET orders with VWAP-accurate fill pricing
- **Monitors** all open positions every 10 seconds and auto-closes on Stop Loss / Take Profit hit
- **Tracks** daily performance, calculates PnL, and enforces a daily loss circuit breaker
- **Exposes** 40+ REST API endpoints for a dashboard frontend to consume

---

## 2. Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Frontend Dashboard                    │
│              (React / Vue / Next.js / etc.)             │
└────────────────────────┬────────────────────────────────┘
                         │ HTTP REST + JSON
┌────────────────────────▼────────────────────────────────┐
│                  TradingBot Web API (.NET 10)            │
│                                                         │
│  Middleware: RateLimiter → ExceptionHandler → Auth      │
│                                                         │
│  10 Controllers:                                        │
│  Trade · Market · Portfolio · Risk · Performance        │
│  AI · Scanner · Indicators · Strategy · System          │
│                                                         │
│  Background Workers:                                    │
│  ├─ SignalGenerationWorker  (every 5 min)               │
│  ├─ TradeMonitoringWorker   (every 10 sec)              │
│  └─ DailyPerformanceWorker  (midnight UTC)              │
└──────────┬──────────────────────────┬───────────────────┘
           │                          │
┌──────────▼──────────┐   ┌──────────▼──────────────────┐
│   SQL Server DB     │   │   Binance API (Polly)        │
│   14 Tables         │   │   retry + circuit breaker    │
│   EF Core / int IDs │   │   BinanceTradeExecution      │
│   decimal(18,8)     │   │   BinanceMarketData          │
└─────────────────────┘   │   BinanceAccount             │
                          └──────────┬──────────────────┘
                                     │
                          ┌──────────▼──────────────────┐
                          │   AI Providers (fallback)   │
                          │   Gemini → Groq →           │
                          │   OpenRouter → Cohere       │
                          └─────────────────────────────┘
```

### Signal Pipeline (every 5 minutes)

```
ScanAllPairsAsync()
  └─ For each active pair:
        CalculateIndicatorsAsync()  →  IndicatorSnapshot (saved to DB)
        EvaluateSignal()            →  Rule-based TradeSignal? (5-gate confluence)
        GetLatestRegimeAsync()      →  Suppress BUY if Bearish / Volatile
        ValidateSignalAsync()       →  AI confidence score (≥ 70 required)
        OpenTradeAsync()            →  MARKET order → Trade saved to DB
```

---

## 3. Project Structure

```
TradingBot/                          ← Web API (startup, controllers, workers)
├─ Controllers/
│   ├─ AIController.cs
│   ├─ IndicatorsController.cs
│   ├─ MarketController.cs
│   ├─ MarketScannerController.cs
│   ├─ PerformanceController.cs
│   ├─ PortfolioController.cs
│   ├─ RiskController.cs
│   ├─ StrategyController.cs
│   ├─ SystemController.cs
│   └─ TradeController.cs
├─ Middleware/
│   └─ GlobalExceptionHandler.cs    ← clean JSON errors + DB logging
├─ Workers/
│   ├─ DailyPerformanceWorker.cs
│   ├─ SignalGenerationWorker.cs
│   └─ TradeMonitoringWorker.cs
├─ Services/
│   ├─ PortfolioManager.cs
│   └─ RiskManagementService.cs
└─ Program.cs

TradingBot.Domain/                   ← Entities, interfaces, enums, models
TradingBot.Application/              ← IndicatorCalculationService, StrategyEngine, MarketScannerService
TradingBot.Infrastructure/           ← Binance clients, AI providers, Polly resilience
TradingBot.Persistence/              ← DbContext, EF migrations, seeders
```

---

## 4. Prerequisites & Setup

### Requirements

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | 10.0+ | `dotnet --version` to verify |
| SQL Server | 2019+ or Express | LocalDB works for dev |
| EF Core CLI | Latest | `dotnet tool install -g dotnet-ef` |
| Binance Account | Testnet recommended | [testnet.binance.vision](https://testnet.binance.vision) |

### First-Time Setup

**1. Clone and restore**
```bash
git clone https://github.com/Mukarram990/TradingBot.git
cd TradingBot
dotnet restore
```

**2. Update the database connection in `TradingBot/appsettings.json`**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=TradingBotDb;Trusted_Connection=True;TrustServerCertificate=True;"
  }
}
```

**3. Set all secrets** (see [Section 5](#5-configuration--secrets))

**4. Run migrations — creates all tables and seeds default data**
```bash
dotnet ef database update -p TradingBot.Persistence -s TradingBot
```

**5. Build and run**
```bash
dotnet run --project TradingBot
```

On every startup the app automatically: applies any pending migrations, seeds the RiskProfile, seeds 5 default trading pairs, and creates today's portfolio baseline snapshot.

---

## 5. Configuration & Secrets

> **Never put API keys in `appsettings.json`.** Use .NET User Secrets locally and environment variables in production.

### Initialize secrets (one time only)
```bash
cd TradingBot
dotnet user-secrets init
```

### Binance
```bash
dotnet user-secrets set "Binance:ApiKey"    "your-testnet-api-key"
dotnet user-secrets set "Binance:ApiSecret" "your-testnet-secret"
```

### AI Providers (configure at least one — Gemini is recommended)
```bash
# Google Gemini — https://aistudio.google.com/apikey  (free, no CC)
dotnet user-secrets set "AI:GeminiApiKey" "AIza..."

# Groq — https://console.groq.com/keys  (free, no CC)
dotnet user-secrets set "AI:GroqApiKey" "gsk_..."

# OpenRouter — https://openrouter.ai/keys  (free :free models)
dotnet user-secrets set "AI:OpenRouterApiKey" "sk-or-..."

# Cohere — https://dashboard.cohere.com/api-keys  (1k req/month free)
dotnet user-secrets set "AI:CohereApiKey" "..."
```

### Authentication
```bash
dotnet user-secrets set "Jwt:SecretKey"  "your-strong-secret-minimum-32-characters"
dotnet user-secrets set "Auth:ApiKey"    "your-admin-api-key"
```

### Production — environment variables
```bash
# Use double-underscore as the section separator
Binance__ApiKey=...
Binance__ApiSecret=...
AI__GeminiApiKey=...
Jwt__SecretKey=...
Auth__ApiKey=...
```

### `appsettings.json` — safe non-secret defaults
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost\\SQLEXPRESS;Database=TradingBotDb;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Binance": {
    "BaseUrl": "https://testnet.binance.vision"
  },
  "AI": {
    "GeminiModel":             "gemini-2.0-flash",
    "GroqModel":               "llama-3.1-70b-versatile",
    "OpenRouterModel":         "meta-llama/llama-3.1-8b-instruct:free",
    "CohereModel":             "command-r",
    "RateLimitBackoffSeconds": 90,
    "MinConfidenceToTrade":    70,
    "ProviderPriority":        ["Gemini", "Groq", "OpenRouter", "Cohere"]
  },
  "Cors": {
    "AllowedOrigin": "http://localhost:3000"
  }
}
```

---

## 6. Running the Application

```bash
# Development
dotnet run --project TradingBot

# Watch mode (auto-restarts on code change)
dotnet watch --project TradingBot run

# Production build
dotnet publish TradingBot -c Release -o ./publish
./publish/TradingBot
```

**Expected startup output:**
```
[INF] Database migrations applied
[INF] RiskProfile seeded
[INF] TradingPairs seeded (5 pairs)
[INF] Portfolio baseline snapshot created for 2026-03-04
[INF] TradeMonitoringWorker started
[INF] SignalGenerationWorker started (first scan in 30s, then every 5 min)
[INF] DailyPerformanceWorker started
[INF] Now listening on: http://localhost:5000
```

**Quick health check:**
```bash
curl http://localhost:5000/api/system/health
```
```json
{
  "status": "healthy",
  "uptimeFormatted": "0h 0m 12s",
  "components": {
    "database":    { "status": "connected" },
    "signalWorker":{ "status": "active (last 5 min)" },
    "tradeMonitor":{ "status": "active (last 5 min)" }
  }
}
```

---

## 7. Complete API Reference

All responses are JSON. All timestamps are UTC ISO 8601. All list endpoints return the standard pagination wrapper: `{ totalCount, page, pageSize, totalPages, data[] }`.

---

### 🔐 Authentication

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/auth/token` | None | Exchange admin API key for JWT |

**Request:**
```json
{ "apiKey": "your-admin-api-key" }
```
**Response:**
```json
{ "token": "eyJ...", "expiresAt": "2026-03-05T00:00:00Z" }
```

All `POST` and `PUT` operations require `Authorization: Bearer <token>`.  
All `GET` operations are public — safe for dashboard polling without auth.

---

### 📈 Trades

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/trades` | Public | Paginated trade list with filters |
| `GET` | `/api/trades/open` | Public | All currently open positions |
| `GET` | `/api/trades/summary` | Public | Aggregated counts + PnL totals |
| `GET` | `/api/trades/{id}` | Public | Single trade with linked orders |
| `GET` | `/api/trades/by-symbol/{symbol}` | Public | All trades for one pair |
| `POST` | `/api/trade/open` | 🔒 JWT | Manually open a trade via Binance |
| `POST` | `/api/trade/close/{id}` | 🔒 JWT | Manually close an open trade |

**GET /api/trades — query params:**
```
?status=2          1=Pending 2=Open 3=Closed 4=Cancelled 5=Failed
?symbol=BTCUSDT
?fromDate=2026-01-01
?toDate=2026-03-04
?page=1
?pageSize=20       max 100
?sortBy=entryTime  entryTime | pnl | symbol
?desc=true
```

**POST /api/trade/open — request body:**
```json
{
  "symbol":       "BTCUSDT",
  "action":       1,
  "entryPrice":   65000.00,
  "stopLoss":     63000.00,
  "takeProfit":   69000.00,
  "aiConfidence": 85
}
```

---

### 🏦 Portfolio

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/portfolio/balance` | Public | Live balance from Binance including today's PnL |
| `GET` | `/api/portfolio/holdings` | Public | Open positions with live unrealized PnL |
| `GET` | `/api/portfolio/snapshots` | Public | Paginated historical snapshot list |
| `GET` | `/api/portfolio/snapshots/today` | Public | Today's opening baseline snapshot |
| `POST` | `/api/portfolio/snapshot` | 🔒 JWT | Create a new snapshot manually |

**GET /api/portfolio/balance — sample response:**
```json
{
  "totalUsdtValue": 10245.50,
  "todayPnL": 245.50,
  "todayPnLPercent": 2.45,
  "baselineBalance": 10000.00,
  "assets": [
    { "asset": "USDT", "free": 9800.00, "locked": 0, "total": 9800.00, "usdtValue": 9800.00 },
    { "asset": "BTC",  "free": 0.007,   "locked": 0, "total": 0.007,  "usdtValue": 445.50  }
  ],
  "fetchedAt": "2026-03-04T10:30:00Z"
}
```

**GET /api/portfolio/holdings — sample response:**
```json
{
  "totalOpenPositions": 1,
  "totalUnrealizedPnL": 32.50,
  "holdings": [
    {
      "tradeId": 42,
      "symbol": "BTCUSDT",
      "quantity": 0.005,
      "entryPrice": 63500.00,
      "currentPrice": 64150.00,
      "stopLoss": 62000.00,
      "takeProfit": 67000.00,
      "unrealizedPnL": 32.50,
      "unrealizedPnLPercent": 1.02,
      "aiConfidence": 82,
      "entryTime": "2026-03-04T08:15:00Z"
    }
  ]
}
```

---

### 📊 Market Data

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/market/price/{symbol}` | Public | Live price for one pair |
| `GET` | `/api/market/prices?symbols=A,B,C` | Public | Live prices for multiple pairs in one call |
| `GET` | `/api/market/candles` | Public | OHLCV candles (from Binance, not stored) |
| `GET` | `/api/market/pairs` | Public | All trading pairs in DB (paginated) |
| `GET` | `/api/market/pairs/active` | Public | Active pairs only (shorthand) |
| `GET` | `/api/market/statistics/{symbol}` | Public | 24h stats for a symbol |
| `GET` | `/api/market/indicators/{symbol}` | Public | Latest saved indicator snapshot |

**GET /api/market/candles — query params:**
```
?symbol=BTCUSDT    required
?interval=1h       1m 5m 15m 30m 1h 4h 1d  (default: 1h)
?limit=100         max 1000
```

---

### 🤖 Indicators

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/indicators/{symbol}/calculate` | 🔒 JWT | Fetch candles + run all 7 indicators + save snapshot |
| `GET` | `/api/indicators/{symbol}/latest` | Public | Most recent saved snapshot from DB |
| `GET` | `/api/indicators/{symbol}/history` | Public | Last N saved snapshots |

**POST query params:**
```
?interval=1h
?candleCount=100   min 50
```

**Indicator snapshot shape:**
```json
{
  "id": 101,
  "symbol": "BTCUSDT",
  "timestamp": "2026-03-04T10:00:00Z",
  "rsi": 42.3,
  "ema20": 64200.00,
  "ema50": 63100.00,
  "macd": 312.50,
  "atr": 820.00,
  "volumeSpike": true,
  "trend": "Uptrend",
  "supportLevel": 62500.00,
  "resistanceLevel": 66000.00
}
```

---

### 🧠 Strategy & Signals

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `POST` | `/api/strategy/evaluate/{symbol}` | 🔒 JWT | Full pipeline: scan → indicators → AI → signal or null |
| `POST` | `/api/strategy/scan-and-evaluate-all` | 🔒 JWT | Evaluate all active pairs in one call |
| `GET` | `/api/strategy/signals` | Public | Recent generated signals (all pairs) |
| `GET` | `/api/strategy/signals/{symbol}` | Public | Recent signals for one pair |

**GET /api/strategy/signals — query params:**
```
?symbol=BTCUSDT   optional filter
?count=20         default 20
```

---

### 🔍 Market Scanner

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/scanner/pairs` | Public | Active pairs the scanner watches |
| `POST` | `/api/scanner/scan/{symbol}` | 🔒 JWT | Scan single pair on demand |
| `POST` | `/api/scanner/scan-all` | 🔒 JWT | Scan all active pairs now |
| `POST` | `/api/scanner/pairs/{symbol}/activate` | 🔒 JWT | Add or re-enable a pair |
| `POST` | `/api/scanner/pairs/{symbol}/deactivate` | 🔒 JWT | Disable a pair |

---

### 🤖 AI Intelligence

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/ai/status` | Public | Provider health + cooldown timers |
| `GET` | `/api/ai/responses` | Public | Paginated AI decision history |
| `GET` | `/api/ai/responses/latest` | Public | Latest AI decision per symbol |
| `POST` | `/api/ai/validate/{symbol}` | 🔒 JWT | Manually trigger AI validation |
| `GET` | `/api/ai/regimes` | Public | Current market regime per pair |
| `POST` | `/api/ai/regime/{symbol}` | 🔒 JWT | Re-detect regime for a symbol |

**GET /api/ai/status — sample response:**
```json
{
  "providers": [
    { "name": "Gemini",     "status": "ready",    "cooldownRemainingSeconds": 0  },
    { "name": "Groq",       "status": "cooldown", "cooldownRemainingSeconds": 45 },
    { "name": "OpenRouter", "status": "ready",    "cooldownRemainingSeconds": 0  },
    { "name": "Cohere",     "status": "ready",    "cooldownRemainingSeconds": 0  }
  ],
  "checkedAt": "2026-03-04T10:30:00Z"
}
```

---

### 📉 Performance

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/performance/daily` | Public | Daily records with optional date-range filter |
| `GET` | `/api/performance/summary` | Public | Aggregated stats for a period |
| `GET` | `/api/performance/statistics` | Public | Breakdown by symbol, hour-of-day, day-of-week |
| `POST` | `/api/performance/calculate` | 🔒 JWT | Recalculate and upsert a specific day |

**GET /api/performance/summary — query params:**
```
?period=all     all | month | week | today
```

**Sample summary response:**
```json
{
  "period": "all",
  "totalTrades": 87,
  "wins": 54,
  "losses": 33,
  "winRate": 62.07,
  "netPnL": 1245.80,
  "avgPnLPerTrade": 14.32,
  "avgWinSize": 42.10,
  "avgLossSize": -18.60,
  "profitFactor": 2.27,
  "maxDrawdown": -210.00,
  "bestTrade": 312.50,
  "worstTrade": -89.40,
  "consecutiveWins": 8,
  "consecutiveLosses": 3,
  "calculatedAt": "2026-03-04T10:30:00Z"
}
```

**GET /api/performance/daily — query params:**
```
?fromDate=2026-01-01
?toDate=2026-03-04
?page=1
?pageSize=30    max 90
```

---

### ⚙️ Risk Management

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/risk/profile` | Public | Current risk configuration |
| `PUT` | `/api/risk/profile` | 🔒 JWT | Update risk configuration live (no restart needed) |

**Risk profile shape:**
```json
{
  "id": 1,
  "maxRiskPerTradePercent": 0.02,
  "maxDailyLossPercent": 0.05,
  "maxTradesPerDay": 5,
  "circuitBreakerLossCount": 3,
  "isEnabled": true
}
```

---

### 🖥️ System & Monitoring

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| `GET` | `/api/system/health` | Public | App health + DB + worker status |
| `GET` | `/api/system/worker-status` | Public | Last activity timestamps for each worker |
| `GET` | `/api/system/database-stats` | Public | Row counts for every table |
| `GET` | `/api/system/logs` | Public | Paginated system log entries |
| `GET` | `/api/system/logs/errors` | Public | Latest ERROR-level entries (last 100) |
| `GET` | `/api/system/logs/{id}` | Public | Full single log entry with stack trace |

**GET /api/system/logs — query params:**
```
?level=ERROR      INFO | WARN | ERROR
?search=BTCUSDT   search within message text
?page=1
?pageSize=50      max 200
```

---

## 8. Response Shapes & Enums

### TradeStatus (int)
| Value | Name |
|-------|------|
| 1 | Pending |
| 2 | Open |
| 3 | Closed |
| 4 | Cancelled |
| 5 | Failed |

### TradeAction (int)
| Value | Name |
|-------|------|
| 1 | Buy |
| 2 | Sell |
| 3 | Hold |

### MarketRegime (string)
`"Trending"` · `"Ranging"` · `"Bearish"` · `"Volatile"`

### Trade object
```json
{
  "id": 42,
  "symbol": "BTCUSDT",
  "entryPrice": 63500.00,
  "exitPrice": 65200.00,
  "quantity": 0.005,
  "stopLoss": 62000.00,
  "takeProfit": 67000.00,
  "pnL": 8.50,
  "pnLPercentage": 2.68,
  "status": 3,
  "entryTime": "2026-03-04T08:15:00Z",
  "exitTime": "2026-03-04T14:30:00Z",
  "aiConfidence": 82,
  "orders": []
}
```

### Order object
```json
{
  "id": 91,
  "tradeId": 42,
  "externalOrderId": "3847261958",
  "symbol": "BTCUSDT",
  "side": "BUY",
  "type": "MARKET",
  "quantity": 0.005,
  "executedPrice": 63512.40,
  "status": 3,
  "createdAt": "2026-03-04T08:15:02Z"
}
```

### DailyPerformance object
```json
{
  "id": 12,
  "date": "2026-03-04T00:00:00Z",
  "totalTrades": 4,
  "wins": 3,
  "losses": 1,
  "winRate": 75.00,
  "netPnL": 124.30,
  "maxDrawdown": 18.20
}
```

---

## 9. Error Handling

No raw stack traces are ever exposed. All errors return a consistent envelope:

**HTTP 400 — Domain / validation error:**
```json
{
  "error": "Bad Request",
  "message": "Daily loss limit exceeded. Trading halted.",
  "requestId": "0HN2VQK7L...",
  "timestamp": "2026-03-04T10:30:00Z"
}
```

**HTTP 401 — Missing or invalid JWT:**
```json
{ "error": "Unauthorized" }
```

**HTTP 429 — Rate limit exceeded:**
```json
{ "error": "Too Many Requests" }
```

**HTTP 500 — Unexpected server error:**
```json
{
  "error": "Internal Server Error",
  "message": "An unexpected error occurred. See server logs for details.",
  "requestId": "0HN2VQK7L...",
  "timestamp": "2026-03-04T10:30:00Z"
}
```

Every 500 error is automatically persisted to the `SystemLogs` table and queryable via `GET /api/system/logs/errors`.

---

## 10. Rate Limiting & Security

### Rate Limiting
- Sliding window: **60 requests / 60 seconds per IP**
- Queue: up to 10 requests buffered, oldest served first
- Exceeding the limit: HTTP 429
- Applied to every controller route via `.RequireRateLimiting("global")`

### Authentication (JWT)
- Mechanism: JWT Bearer token (HS256)
- Obtain: `POST /api/auth/token` with your admin API key
- Header: `Authorization: Bearer <token>`
- **Protected:** All `POST` and `PUT` endpoints
- **Public:** All `GET` endpoints — safe for unauthenticated dashboard polling

### Binance HTTP Resilience (Polly)
- **Retry:** 3 attempts, exponential backoff (1s → 2s → 4s + jitter)
- **Circuit breaker:** Opens at 50% failure rate over 30s, resets after 15s
- **Never retried:** HTTP 400, 401, 403

### AI Provider Resilience
- **Per-provider retry:** 2 attempts on network errors only
- **Rate-limit (429) handling:** 90-second in-memory cooldown per provider, automatic rotation to next
- **Fallback chain:** Gemini → Groq → OpenRouter → Cohere → skip trade (never crashes the worker)

### Credentials
- All API keys in .NET User Secrets locally, environment variables in production
- Nothing sensitive in `appsettings.json` or source control

---

## 11. Background Workers

| Worker | Interval | Responsibility |
|--------|----------|---------------|
| `TradeMonitoringWorker` | Every **10 seconds** | Fetches live prices for all open trades; auto-closes on SL or TP hit |
| `SignalGenerationWorker` | Every **5 minutes** | Full pipeline: scan → indicators → strategy → AI → Binance order |
| `DailyPerformanceWorker` | **Midnight UTC** + startup back-fill | Calculates and upserts `DailyPerformance` record for the previous day |

Monitor status in real time:
```bash
GET /api/system/worker-status   # last-seen timestamps + running/unknown for each worker
GET /api/system/health          # quick active/idle signal
```

---

## 12. Database Schema Overview

| Table | Purpose |
|-------|---------|
| `Trades` | Core trade lifecycle: Open → Closed |
| `Orders` | Individual Binance fills linked to a Trade |
| `TradingPairs` | Pairs the scanner is configured to watch |
| `IndicatorSnapshots` | Saved technical indicator results per symbol per tick |
| `TradeSignals` | Generated signals with rule-based confidence scores |
| `AIResponses` | Full AI provider responses: action, confidence, reasoning, model used |
| `MarketRegimes` | AI-classified market conditions per pair |
| `PortfolioSnapshots` | Daily USDT balance baselines |
| `DailyPerformances` | Aggregated PnL, win rate, drawdown per calendar day |
| `RiskProfile` | Configurable risk parameters |
| `SystemLogs` | Application logs written by workers and exception handler |
| `Strategy` | Strategy metadata (future use) |
| `UserAccount` | Account management (future use) |

**Key schema decisions:**
- All monetary / price columns: `decimal(18,8)` — correct precision for crypto
- All IDs: `int IDENTITY(1,1)` auto-increment
- All timestamps: `datetime2` UTC
- Indexes on: `Trades(Symbol)`, `Orders(TradeId)`, `TradingPairs(Symbol)` unique, `IndicatorSnapshots(Symbol, Timestamp)`, `TradeSignals(Symbol, CreatedAt)`, `DailyPerformances(Date)` unique

---

## 13. Frontend Integration Guide

### Recommended Dashboard Pages

| Page | Primary Endpoints |
|------|------------------|
| **Overview / Home** | `GET /api/system/health`, `GET /api/portfolio/balance`, `GET /api/trades/open`, `GET /api/performance/summary?period=today` |
| **Live Positions** | `GET /api/portfolio/holdings` (poll every 15s) |
| **Trade History** | `GET /api/trades?status=3&page=1` |
| **Market & Indicators** | `GET /api/market/prices?symbols=BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT,XRPUSDT`, `GET /api/indicators/{symbol}/latest` |
| **Performance Analytics** | `GET /api/performance/summary`, `GET /api/performance/daily`, `GET /api/performance/statistics` |
| **AI Dashboard** | `GET /api/ai/status`, `GET /api/ai/responses`, `GET /api/ai/regimes` |
| **Signals Feed** | `GET /api/strategy/signals` (poll every 30s) |
| **System Monitor** | `GET /api/system/worker-status`, `GET /api/system/logs/errors`, `GET /api/system/database-stats` |
| **Risk Settings** | `GET /api/risk/profile`, `PUT /api/risk/profile` |

### Suggested Polling Intervals

| Data | Interval | Endpoint |
|------|----------|----------|
| Open positions + unrealized PnL | 15 s | `GET /api/portfolio/holdings` |
| Ticker prices | 10 s | `GET /api/market/prices?symbols=...` |
| Worker health | 60 s | `GET /api/system/worker-status` |
| AI provider status | 30 s | `GET /api/ai/status` |
| Latest signals | 60 s | `GET /api/strategy/signals` |
| System health badge | 30 s | `GET /api/system/health` |
| Trade history / performance | On demand | various |

### CORS
Configured for `http://localhost:3000` by default. To change:
```json
// appsettings.json
"Cors": { "AllowedOrigin": "https://your-dashboard.com" }
```

### Authentication Flow
```
1. Login screen → POST /api/auth/token  { "apiKey": "..." }
2. Store JWT in memory (avoid localStorage — XSS risk)
3. Attach to every mutating request: Authorization: Bearer <token>
4. On 401 response → redirect to login
5. Check expiresAt and refresh before expiry
```

### TypeScript API Client (quick start)
```typescript
const BASE_URL = 'http://localhost:5000';

let _token: string | null = null;

export function setToken(token: string) { _token = token; }

export async function api<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE_URL}${path}`, {
    headers: {
      'Content-Type': 'application/json',
      ...(_token ? { Authorization: `Bearer ${_token}` } : {}),
    },
    ...options,
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error((err as any).message ?? `HTTP ${res.status}`);
  }

  return res.json() as Promise<T>;
}

// Usage examples
const health    = await api('/api/system/health');
const holdings  = await api('/api/portfolio/holdings');
const trades    = await api('/api/trades?status=3&page=1&pageSize=20');
const summary   = await api('/api/performance/summary?period=month');
const aiStatus  = await api('/api/ai/status');

// Authenticated mutation
const token = await api<{ token: string }>('/api/auth/token', {
  method: 'POST',
  body: JSON.stringify({ apiKey: 'your-key' }),
});
setToken(token.token);

await api('/api/trade/close/42', { method: 'POST' });
```

---

## 14. Development Notes

### Switching Binance Testnet ↔ Live
```bash
# Testnet (default — fake money, safe for testing)
# appsettings.json: "BaseUrl": "https://testnet.binance.vision"
dotnet user-secrets set "Binance:ApiKey" "testnet-api-key"

# Live trading (real money — only after thorough paper trading)
# appsettings.json: "BaseUrl": "https://api.binance.com"
dotnet user-secrets set "Binance:ApiKey" "live-api-key"
```

### Adding a New Trading Pair
```bash
# Via API (no restart needed)
POST /api/scanner/pairs/ADAUSDT/activate
```

### Changing Risk Parameters Live
```bash
# No code change or restart required
PUT /api/risk/profile
{
  "maxRiskPerTradePercent": 0.01,
  "maxDailyLossPercent": 0.03,
  "maxTradesPerDay": 3,
  "circuitBreakerLossCount": 2,
  "isEnabled": true
}
```

### Viewing Logs
```bash
# Console output (all levels)
dotnet run --project TradingBot

# Daily rolling file
cat logs/tradingbot-20260304.log

# Database — errors only
GET /api/system/logs/errors

# Database — searchable, all levels
GET /api/system/logs?level=ERROR&search=BTCUSDT
```

### Database Migrations
```bash
# Apply all pending migrations
dotnet ef database update -p TradingBot.Persistence -s TradingBot

# Create a new migration after entity changes
dotnet ef migrations add YourMigrationName -p TradingBot.Persistence -s TradingBot

# Roll back one migration
dotnet ef database update PreviousMigrationName -p TradingBot.Persistence -s TradingBot
```

### Verify Worker Health
```bash
curl http://localhost:5000/api/system/worker-status
# All 3 workers should show status "running"
```

### Default Seeded Trading Pairs
On first startup the system seeds: `BTCUSDT`, `ETHUSDT`, `BNBUSDT`, `SOLUSDT`, `XRPUSDT`.  
Add or remove pairs at runtime via `POST /api/scanner/pairs/{symbol}/activate` and `/deactivate`.