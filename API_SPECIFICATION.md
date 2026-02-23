# TradingBot API Specification - Complete Reference

## 📌 API Overview

**Base URL**: `http://localhost:5000/api`

**Status**: 70% Complete (24 of 30 endpoints implemented)

---

## ✅ IMPLEMENTED ENDPOINTS

### 🏪 Trade Management

#### 1. Open Trade
```
POST /trade/open
Content-Type: application/json

Request Body:
{
  "symbol": "BTCUSDT",          // Trading pair
  "action": 1,                  // 1=Buy, 2=Sell, 3=Hold
  "entryPrice": 43250.50,       // Entry price
  "stopLoss": 42900.00,         // Stop loss
  "takeProfit": 43600.00,       // Take profit
  "quantity": 0.001,            // Quantity (will be overridden by position sizing)
  "accountBalance": 1000,       // Current balance
  "aiConfidence": 85            // Confidence percentage (0-100)
}

Response (200 OK):
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "externalOrderId": "1234567890",
  "symbol": "BTCUSDT",
  "quantity": 0.00234,           // Calculated by 2% rule
  "executedPrice": 43250.50,
  "status": 2,                   // 2=Open
  "tradeId": "550e8400-e29b-41d4-a716-446655440001",
  "createdAt": "2025-02-16T10:30:00Z"
}

Errors:
- 400: Invalid stop loss
- 400: Max trades per day reached
- 400: Circuit breaker triggered
- 400: Daily loss limit exceeded
- 400: Insufficient balance
```

#### 2. Close Trade
```
POST /trade/close/{tradeId}
Content-Type: application/json

Path Parameters:
- tradeId: Guid of the trade to close

Response (200 OK):
{
  "id": "550e8400-e29b-41d4-a716-446655440002",
  "externalOrderId": "1234567891",
  "symbol": "BTCUSDT",
  "quantity": 0.00234,
  "executedPrice": 43500.00,     // Exit price
  "status": 3,                    // 3=Closed
  "tradeId": "550e8400-e29b-41d4-a716-446655440001",
  "createdAt": "2025-02-16T10:35:00Z"
}

Trade is updated with:
- ExitPrice: 43500.00
- ExitTime: now
- Status: Closed
- PnL: (43500 - 43250.50) * 0.00234 = 0.5862 USDT
- PnLPercentage: 0.5749%

Errors:
- 404: Trade not found
- 400: Trade is not open
- 400: Binance order failed
```

---

### 📊 Market Data

#### 3. Get Current Price
```
GET /market/price/{symbol}

Path Parameters:
- symbol: Trading pair (e.g., BTCUSDT)

Response (200 OK):
43250.50

Errors:
- 404: Trading pair not found
- 500: Binance API error
```

#### 4. Get Candles (OHLCV)
```
GET /market/candles?symbol=BTCUSDT&interval=1h&limit=100

Query Parameters:
- symbol: Trading pair (required)
- interval: 1m, 5m, 15m, 1h, 4h, 1d, etc. (required)
- limit: 1-1000, default 100 (optional)

Response (200 OK):
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "symbol": "BTCUSDT",
    "interval": "1h",
    "openTime": "2025-02-16T10:00:00Z",
    "closeTime": "2025-02-16T11:00:00Z",
    "open": 43200.00,
    "high": 43350.00,
    "low": 43100.00,
    "close": 43250.00,
    "volume": 125.50,
    "createdAt": "2025-02-16T10:00:00Z"
  },
  ...
]

Errors:
- 400: Invalid symbol
- 400: Limit must be 1-1000
- 500: Binance API error
```

---

### 💼 Portfolio Management

#### 5. Create Portfolio Snapshot
```
POST /portfolio/snapshot
Content-Type: application/json

Request Body: {} (empty)

Response (200 OK):
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "totalBalanceUSDT": 9850.25,      // Total account value
  "totalUnrealizedPnL": -149.75,    // Current unrealized PnL
  "totalOpenPositions": 2,          // Number of open trades
  "dailyPnL": 50.00,                // PnL today
  "createdAt": "2025-02-16T10:30:00Z"
}

Purpose: 
- Serves as daily baseline for loss limit enforcement
- Creates audit trail of account state
- Used for performance analytics
```

---

### ⚙️ Risk Management

#### 6. Get Risk Profile
```
GET /risk/profile

Response (200 OK):
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "maxRiskPerTradePercent": 0.02,      // 2%
  "maxDailyLossPercent": 0.05,         // 5%
  "maxTradesPerDay": 5,
  "circuitBreakerLossCount": 3,
  "isEnabled": true,
  "createdAt": "2025-02-16T10:00:00Z"
}
```

#### 7. Update Risk Profile
```
PUT /risk/profile
Content-Type: application/json

Request Body:
{
  "maxRiskPerTradePercent": 0.025,
  "maxDailyLossPercent": 0.06,
  "maxTradesPerDay": 6,
  "circuitBreakerLossCount": 2,
  "isEnabled": true
}

Response (200 OK):
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "maxRiskPerTradePercent": 0.025,
  "maxDailyLossPercent": 0.06,
  "maxTradesPerDay": 6,
  "circuitBreakerLossCount": 2,
  "isEnabled": true,
  "updatedAt": "2025-02-16T10:30:00Z"
}

Impact: Changes take effect immediately on next trade
```

---

## ❌ MISSING ENDPOINTS (To Implement)

### Trade Queries

#### Missing #1: Get Single Trade
```
GET /trade/{tradeId}

Path Parameters:
- tradeId: Guid of trade

Expected Response (200 OK):
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "symbol": "BTCUSDT",
  "entryPrice": 43250.50,
  "exitPrice": 43500.00,
  "quantity": 0.00234,
  "stopLoss": 42900.00,
  "takeProfit": 43600.00,
  "pnL": 0.5862,
  "pnLPercentage": 0.5749,
  "status": 3,                    // 3=Closed
  "entryTime": "2025-02-16T10:30:00Z",
  "exitTime": "2025-02-16T10:35:00Z",
  "aiConfidence": 85,
  "orders": [
    {
      "id": "...",
      "externalOrderId": "...",
      "quantity": 0.00234,
      "executedPrice": 43250.50,
      "status": 2,
      "createdAt": "2025-02-16T10:30:00Z"
    },
    {
      "id": "...",
      "externalOrderId": "...",
      "quantity": 0.00234,
      "executedPrice": 43500.00,
      "status": 3,
      "createdAt": "2025-02-16T10:35:00Z"
    }
  ],
  "createdAt": "2025-02-16T10:30:00Z"
}
```

#### Missing #2: List Trades
```
GET /trade?page=1&limit=20&status=3&symbol=BTCUSDT&fromDate=2025-02-01&toDate=2025-02-28

Query Parameters (all optional):
- page: Page number (default 1)
- limit: Items per page (default 20, max 100)
- status: 1=Pending, 2=Open, 3=Closed, 4=Cancelled, 5=Failed
- symbol: Filter by symbol (e.g., BTCUSDT)
- fromDate: Filter from date (ISO 8601)
- toDate: Filter to date (ISO 8601)

Expected Response (200 OK):
{
  "totalCount": 150,
  "page": 1,
  "pageSize": 20,
  "trades": [
    { ... },
    { ... }
  ]
}
```

#### Missing #3: Get Trade Orders
```
GET /trade/{tradeId}/orders

Path Parameters:
- tradeId: Guid of trade

Expected Response (200 OK):
[
  {
    "id": "...",
    "externalOrderId": "1234567890",
    "symbol": "BTCUSDT",
    "quantity": 0.00234,
    "executedPrice": 43250.50,
    "status": 2,
    "createdAt": "2025-02-16T10:30:00Z"
  },
  {
    "id": "...",
    "externalOrderId": "1234567891",
    "symbol": "BTCUSDT",
    "quantity": 0.00234,
    "executedPrice": 43500.00,
    "status": 3,
    "createdAt": "2025-02-16T10:35:00Z"
  }
]
```

---

### Portfolio Queries

#### Missing #4: Get Portfolio Snapshots
```
GET /portfolio/snapshots?page=1&limit=30&fromDate=2025-02-01

Query Parameters:
- page: Page number (default 1)
- limit: Items per page (default 30)
- fromDate: Filter from date

Expected Response (200 OK):
{
  "totalCount": 45,
  "page": 1,
  "pageSize": 30,
  "snapshots": [
    {
      "id": "...",
      "totalBalanceUSDT": 9850.25,
      "totalUnrealizedPnL": -149.75,
      "totalOpenPositions": 2,
      "dailyPnL": 50.00,
      "createdAt": "2025-02-16T10:30:00Z"
    },
    ...
  ]
}
```

#### Missing #5: Get Portfolio Summary
```
GET /portfolio/summary

Expected Response (200 OK):
{
  "totalBalance": 9850.25,
  "availableBalance": 8500.00,
  "lockedBalance": 1350.25,
  "openPositions": [
    {
      "symbol": "BTCUSDT",
      "quantity": 0.001,
      "averageEntry": 43250.00,
      "currentPrice": 43500.00,
      "unrealizedPnL": 0.25,
      "unrealizedPnLPercent": 0.58
    },
    {
      "symbol": "ETHUSDT",
      "quantity": 0.05,
      "averageEntry": 2250.00,
      "currentPrice": 2280.00,
      "unrealizedPnL": 1.50,
      "unrealizedPnLPercent": 1.33
    }
  ],
  "totalUnrealizedPnL": 1.75,
  "totalRealizedPnL": 50.00,
  "dailyChange": 51.75,
  "dailyChangePercent": 0.53
}
```

#### Missing #6: Get Portfolio Holdings
```
GET /portfolio/holdings?symbol=BTCUSDT

Query Parameters:
- symbol: Optional - filter by specific holding

Expected Response (200 OK):
{
  "holdings": [
    {
      "symbol": "BTCUSDT",
      "quantity": 0.001,
      "averageEntry": 43250.00,
      "currentPrice": 43500.00,
      "unrealizedPnL": 0.25,
      "percentOfPortfolio": 5.2,
      "lastUpdated": "2025-02-16T10:35:00Z"
    },
    {
      "symbol": "USDT",
      "quantity": 8500.00,
      "averageEntry": 1.00,
      "currentPrice": 1.00,
      "unrealizedPnL": 0.00,
      "percentOfPortfolio": 86.2,
      "lastUpdated": "2025-02-16T10:35:00Z"
    }
  ]
}
```

---

### Performance & Analytics

#### Missing #7: Get Daily Performance
```
GET /performance/daily?fromDate=2025-02-01&toDate=2025-02-28&page=1&limit=30

Query Parameters:
- fromDate: Start date (ISO 8601, required)
- toDate: End date (ISO 8601, required)
- page: Page number (default 1)
- limit: Items per page (default 30)

Expected Response (200 OK):
{
  "totalCount": 28,
  "data": [
    {
      "id": "...",
      "date": "2025-02-16",
      "totalTrades": 5,
      "wins": 3,
      "losses": 2,
      "winRate": 60.0,
      "netPnL": 125.50,
      "maxDrawdown": 50.00,
      "createdAt": "2025-02-17T00:00:00Z"
    },
    ...
  ]
}
```

#### Missing #8: Get Performance Summary
```
GET /performance/summary?period=all

Query Parameters:
- period: all, month, week, day (default: all)

Expected Response (200 OK):
{
  "totalTrades": 150,
  "totalWins": 95,
  "totalLosses": 55,
  "winRate": 63.33,
  "totalPnL": 1250.75,
  "averagePnLPerTrade": 8.34,
  "averageWinSize": 15.50,
  "averageLossSize": -12.30,
  "profitFactor": 2.15,        // Total wins / Total losses
  "maxDrawdown": 300.00,
  "sharpeRatio": 1.85,
  "sortinoRatio": 2.40,
  "bestTrade": 125.50,
  "worstTrade": -95.00,
  "consecutiveWins": 5,
  "consecutiveLosses": 2,
  "averageHoldingTime": "2h 15m"
}
```

#### Missing #9: Get Trade Statistics
```
GET /performance/statistics

Expected Response (200 OK):
{
  "bySymbol": {
    "BTCUSDT": {
      "trades": 45,
      "wins": 28,
      "losses": 17,
      "winRate": 62.22,
      "netPnL": 450.00,
      "avgPnL": 10.00
    },
    "ETHUSDT": {
      "trades": 60,
      "wins": 42,
      "losses": 18,
      "winRate": 70.00,
      "netPnL": 600.00,
      "avgPnL": 10.00
    }
  },
  "byHour": {
    "00": { "trades": 8, "wins": 5, "losses": 3 },
    "01": { "trades": 12, "wins": 8, "losses": 4 },
    ...
  },
  "byDayOfWeek": {
    "Monday": { "trades": 25, "wins": 16, "losses": 9 },
    ...
  }
}
```

---

### Market Data

#### Missing #10: Get Exchange Info
```
GET /market/pairs?limit=50&offset=0&active=true

Query Parameters:
- limit: Max items (default 50)
- offset: Skip items (default 0)
- active: Filter active only (default true)

Expected Response (200 OK):
{
  "totalCount": 2000,
  "pairs": [
    {
      "id": "...",
      "symbol": "BTCUSDT",
      "baseAsset": "BTC",
      "quoteAsset": "USDT",
      "minQty": 0.001,
      "stepSize": 0.00001,
      "isActive": true
    },
    ...
  ]
}
```

#### Missing #11: Get Market Statistics
```
GET /market/statistics?period=24h

Query Parameters:
- period: 1h, 24h, 7d (default 24h)

Expected Response (200 OK):
[
  {
    "symbol": "BTCUSDT",
    "price": 43250.50,
    "priceChange": 250.50,
    "priceChangePercent": 0.59,
    "highPrice": 43500.00,
    "lowPrice": 42800.00,
    "volume": 12500.50,
    "quoteAssetVolume": 540000000.00
  },
  ...
]
```

---

### Signals & Strategies

#### Missing #12: Get Trade Signals
```
GET /signals?symbol=BTCUSDT&status=pending&limit=20

Query Parameters:
- symbol: Filter by symbol (optional)
- status: pending, executed, rejected
- limit: Max items (default 20)

Expected Response (200 OK):
[
  {
    "id": "...",
    "symbol": "BTCUSDT",
    "action": 1,                 // 1=Buy, 2=Sell, 3=Hold
    "confidence": 85.5,
    "quantity": 0.00234,
    "entryPrice": 43250.00,
    "stopLoss": 42900.00,
    "takeProfit": 43600.00,
    "status": "pending",
    "createdAt": "2025-02-16T10:30:00Z"
  }
]
```

#### Missing #13: Get Strategy Performance
```
GET /strategy/{strategyId}/performance

Expected Response (200 OK):
{
  "strategyId": "...",
  "name": "RSI+EMA Strategy",
  "totalTrades": 45,
  "wins": 28,
  "losses": 17,
  "winRate": 62.22,
  "netPnL": 450.00,
  "startDate": "2025-01-16",
  "endDate": "2025-02-16"
}
```

---

### Health & Status

#### Missing #14: Health Check
```
GET /health

Expected Response (200 OK):
{
  "status": "healthy",
  "timestamp": "2025-02-16T10:35:00Z",
  "database": "connected",
  "binance": "connected",
  "uptimeSeconds": 86400,
  "activeConnections": 2
}
```

---

## 📊 DATA TYPES & ENUMS

### TradeStatus Enum
```
1 = Pending  (not used yet)
2 = Open     (position is active)
3 = Closed   (position exited)
4 = Cancelled
5 = Failed
```

### TradeAction Enum
```
1 = Buy    (long entry)
2 = Sell   (exit/short - not implemented yet)
3 = Hold   (no action)
```

### OrderStatus
```
Same as TradeStatus (uses TradeStatus enum)
```

---

## 🔐 Authentication

**Current Status**: ❌ NOT IMPLEMENTED

**Planned**:
- JWT bearer token
- API key authentication
- Role-based access control

For now, all endpoints are public (development only).

---

## 📈 Pagination Convention

All list endpoints support:
```
{
  "totalCount": 150,        // Total items in database
  "page": 1,               // Current page
  "pageSize": 20,          // Items per page
  "data": [...]            // Array of items
}
```

---

## ⚠️ Common Error Responses

### 400 Bad Request
```json
{
  "error": "Invalid request",
  "message": "Stop loss must be less than entry price",
  "details": "..."
}
```

### 404 Not Found
```json
{
  "error": "Resource not found",
  "message": "Trade with ID '...' not found"
}
```

### 500 Internal Server Error
```json
{
  "error": "Internal server error",
  "message": "An unexpected error occurred",
  "requestId": "..."
}
```

---

## 🧪 Quick Test Script

```bash
#!/bin/bash

API="http://localhost:5000/api"

# 1. Get current price
curl -X GET "$API/market/price/BTCUSDT"

# 2. Get candles
curl -X GET "$API/market/candles?symbol=BTCUSDT&interval=1h&limit=10"

# 3. Get risk profile
curl -X GET "$API/risk/profile"

# 4. Create portfolio snapshot
curl -X POST "$API/portfolio/snapshot"

# 5. Open trade
curl -X POST "$API/trade/open" \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "action": 1,
    "entryPrice": 43250,
    "stopLoss": 42900,
    "takeProfit": 43600,
    "quantity": 0.001,
    "aiConfidence": 85
  }'
```

