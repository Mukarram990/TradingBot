# 📊 Phase 2 Implementation Plan — Automation Layer

## ✅ What's Already Done (Phase 1 — 100% Complete)

| # | Item | File(s) |
|---|------|---------|
| 1 | Binance trade execution (open/close) | `BinanceTradeExecutionService.cs` |
| 2 | Real-time market data (price, candles) | `BinanceMarketDataService.cs` |
| 3 | Position sizing (2% risk rule) | `RiskManagementService.cs` |
| 4 | Daily loss limit enforcement (5%) | `RiskManagementService.cs` + `PortfolioManager.cs` |
| 5 | SL/TP auto-close background worker | `TradeMonitoringService.cs` + `TradeMonitoringWorker.cs` |
| 6 | Circuit breaker (N consecutive losses) | `RiskManagementService.cs` |
| 7 | Risk profile stored in DB + REST API | `RiskController.cs` + `RiskProfileSeeder.cs` |
| 8 | Credentials secured via user-secrets | `appsettings.json` |
| 9 | int IDENTITY primary keys all tables | `BaseEntity.cs` |
| 10 | Auto-migration + seeding on startup | `Program.cs` |
| 11 | Portfolio snapshots (daily baseline) | `PortfolioManager.cs` |

---

## 🔨 Phase 2 — What We Build Next (Automation)

The goal of Phase 2 is to make the bot **self-driving**: it discovers opportunities,
calculates technical indicators, generates signals, and submits trades — all automatically.

### Build Order (step by step)

```
Step 1 → IndicatorCalculationService     (compute RSI, EMA, MACD, ATR from candles)
Step 2 → MarketScannerService            (fetch pairs + candles, call indicators)
Step 3 → StrategyEngine                  (evaluate indicators → TradeSignal)
Step 4 → SignalGenerationWorker          (background job, runs every 5 min)
Step 5 → GET /api/trades + /api/signals  (missing read endpoints)
```

---

## Step 1 — IndicatorCalculationService

**File**: `TradingBot.Application/IndicatorCalculationService.cs`  
(Skeleton already exists — we fill it in)

**Interface already exists**: `TradingBot.Domain/Interfaces/IIndicatorService.cs`

**Entity already exists**: `TradingBot.Domain/Entities/IndicatorSnapshot.cs`

### What to implement:

```
RSI (Relative Strength Index)
  - Period: 14
  - Formula: 100 - (100 / (1 + RS)) where RS = avg gain / avg loss
  - Signal: RSI < 30 = oversold (buy zone), RSI > 70 = overbought (sell zone)

EMA20 (Exponential Moving Average - 20 periods)
  - Multiplier: 2 / (20 + 1)
  - Short-term trend direction

EMA50 (Exponential Moving Average - 50 periods)
  - Multiplier: 2 / (50 + 1)
  - Medium-term trend direction
  - Signal: EMA20 > EMA50 = uptrend, EMA20 < EMA50 = downtrend

MACD (Moving Average Convergence Divergence)
  - MACD Line = EMA12 - EMA26
  - Signal Line = EMA9 of MACD Line
  - Histogram = MACD Line - Signal Line
  - Signal: MACD crosses above Signal Line = bullish

ATR (Average True Range)
  - Period: 14
  - Measures volatility
  - Used for dynamic SL/TP calculation

Volume Spike
  - Compare current volume vs average of last 20 candles
  - Spike = current volume > 1.5x average

Support & Resistance (simple)
  - Support = lowest low of last N candles
  - Resistance = highest high of last N candles
```

### Output — saves to `IndicatorSnapshot` table:
```json
{
  "symbol": "BTCUSDT",
  "timestamp": "2026-02-23T10:00:00Z",
  "rsi": 42.5,
  "ema20": 43100.0,
  "ema50": 42800.0,
  "macd": 45.2,
  "atr": 320.0,
  "volumeSpike": false,
  "trend": "Uptrend",
  "supportLevel": 41500.0,
  "resistanceLevel": 44200.0
}
```

---

## Step 2 — MarketScannerService

**File**: `TradingBot.Application/MarketScannerService.cs`  
(Skeleton already exists — we fill it in)

### What to implement:

```
1. GetActivePairsAsync()
   → Read TradingPairs table (or hardcode initial list: BTCUSDT, ETHUSDT, BNBUSDT)
   → Filter: IsActive = true

2. ScanPairAsync(string symbol)
   → Fetch last 50 candles (1h interval) from BinanceMarketDataService
   → Pass to IndicatorCalculationService
   → Save IndicatorSnapshot to DB
   → Return snapshot

3. ScanAllPairsAsync()
   → Loop through active pairs
   → Call ScanPairAsync for each
   → Return List<IndicatorSnapshot>
```

---

## Step 3 — StrategyEngine

**File**: `TradingBot/Services/StrategyEngine.cs` (new file)

**Interface**: `TradingBot.Domain/Interfaces/IStrategyEngine.cs` (new file)

### What to implement:

```
EvaluateSignal(IndicatorSnapshot snapshot) → TradeSignal?

BUY conditions (all must be true):
  ✔ RSI < 45 (not overbought)
  ✔ EMA20 > EMA50 (uptrend)
  ✔ MACD histogram > 0 (bullish momentum)
  ✔ Volume spike OR price near support

SELL / SKIP conditions (any = no trade):
  ✗ RSI > 70
  ✗ EMA20 < EMA50 (downtrend)
  ✗ MACD histogram < 0

Confidence score (0-100):
  Each passing condition adds points
  Only trade if confidence >= 70

SL/TP calculation:
  StopLoss    = entryPrice - (ATR * 1.5)
  TakeProfit  = entryPrice + (ATR * 3.0)   ← 2:1 risk/reward
```

---

## Step 4 — SignalGenerationWorker

**File**: `TradingBot/Workers/SignalGenerationWorker.cs` (new file)

### What to implement:

```
Runs every 5 minutes (BackgroundService)

On each tick:
  1. Call MarketScannerService.ScanAllPairsAsync()
  2. For each IndicatorSnapshot:
       a. Call StrategyEngine.EvaluateSignal(snapshot)
       b. If signal returned (not null):
            - Check RiskManagement: CanTradeToday()? CircuitBreaker?
            - If OK: call TradeExecutionService.OpenTradeAsync(signal)
            - Log result to SystemLog
  3. Wait 5 minutes, repeat
```

---

## Step 5 — Missing Read Endpoints

These GET endpoints are needed for monitoring and future UI:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/trades` | List all trades (with optional filters: status, date) |
| `GET` | `/api/trades/{id}` | Get single trade with its orders |
| `GET` | `/api/signals` | List recent trade signals |
| `GET` | `/api/portfolio/balance` | Current live balance from Binance |
| `GET` | `/api/portfolio/snapshots` | Historical portfolio snapshots |

---

## Summary — What We Implement Together

| Step | Effort | Depends On |
|------|--------|-----------|
| 1. IndicatorCalculationService | Medium | BinanceMarketDataService ✅ |
| 2. MarketScannerService | Small | Step 1 |
| 3. StrategyEngine | Medium | Step 1, 2 |
| 4. SignalGenerationWorker | Small | Step 2, 3 |
| 5. Missing read endpoints | Small | existing controllers |

**Start with Step 1** — everything else flows from it.
