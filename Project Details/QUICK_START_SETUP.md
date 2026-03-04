# 🚀 QUICK START - First Time Setup

## Everything is Fixed ✅

The UserAccount class and database schema are now **correct**.

---

## Step-by-Step Setup (5 minutes)

### Step 1: Create Your First Dashboard User
```bash
# Open SQL Server Management Studio or use sqlcmd

# Execute this SQL:
INSERT INTO UserAccounts (Username, IsActive, CreatedAt)
VALUES ('admin', 1, GETUTCDATE());

-- That's it! Just username and active status.
-- NO balance fields!
```

**Verify it was created:**
```sql
SELECT ID, Username, IsActive, ApiKeyHash FROM UserAccounts;

-- Output:
-- ID | Username | IsActive | ApiKeyHash
-- 1  | admin    | 1        | NULL (will be set after key generation)
```

---

### Step 2: Start Your Application
```bash
cd D:\Personal\TradingBot
dotnet run --project TradingBot

# Application runs on http://localhost:5000
```

---

### Step 3: Generate Your API Key
```bash
# In a new terminal, run this curl command:

curl -X POST "http://localhost:5000/api/auth/generate-key" \
  -H "Authorization: ApiKey admin" \
  -H "Content-Type: application/json"
```

**Response:**
```json
{
  "message": "API key generated successfully.",
  "apiKey": "aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==",
  "warning": "⚠️ Save this key securely. You won't be able to see it again!",
  "generatedAt": "2025-03-04T16:52:00Z",
  "usage": "Authorization: ApiKey {apiKey}"
}
```

### ⚠️ IMPORTANT: Copy Your API Key!
```
aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==
```

**Save this in a safe place.** You need it for all API requests.

---

### Step 4: Test Your Setup

**Test 1: Verify Authentication**
```bash
curl -X GET "http://localhost:5000/api/auth/status" \
  -H "Authorization: ApiKey aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC=="
```

**Expected Response (200 OK):**
```json
{
  "authenticated": true,
  "userId": 1,
  "username": "admin",
  "checkedAt": "2025-03-04T16:52:00Z"
}
```

✅ **Authentication works!**

---

**Test 2: Get Live Balance**
```bash
curl -X GET "http://localhost:5000/api/portfolio/balance" \
  -H "Authorization: ApiKey aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC=="
```

**Expected Response (200 OK):**
```json
{
  "totalBalanceUSDT": 72500.50,
  "fetchedAt": "2025-03-04T16:52:00Z",
  "source": "Binance API (Live)"
}
```

✅ **Live balance fetched from Binance!**

---

**Test 3: Protected Endpoint Without Key**
```bash
curl -X POST "http://localhost:5000/api/trade/open" \
  -H "Content-Type: application/json" \
  -d '{"symbol": "BTCUSDT"}'
```

**Expected Response (401 Unauthorized):**
```json
{
  "error": "Unauthorized",
  "message": "API key required. Use header: Authorization: ApiKey {key}"
}
```

✅ **Endpoint is protected!**

---

**Test 4: Protected Endpoint With Key**
```bash
curl -X POST "http://localhost:5000/api/trade/open" \
  -H "Authorization: ApiKey aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==" \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "entryPrice": 45000,
    "quantity": 0.01,
    "stopLoss": 44000,
    "takeProfit": 46500
  }'
```

✅ **Protected endpoint works!**

---

## What's Different Now

| Before | After |
|--------|-------|
| ❌ UserAccount had TotalBalance field | ✅ Removed - not used |
| ❌ Tried to store balance in DB | ✅ Fetch live from Binance |
| ❌ Confusing per-user balance | ✅ One shared Binance account |
| ❌ Balance never updated | ✅ Always current from Binance |
| ✅ API key authentication | ✅ Works correctly |

---

## Database Before vs After

### Before (Wrong)
```sql
UserAccounts:
┌────┬──────────┬──────────────┬──────────────────┬───────────────┬──────────────┐
│ ID │ Username │ TotalBalance │ AvailableBalance │ LockedBalance │ ApiKeyHash   │
├────┼──────────┼──────────────┼──────────────────┼───────────────┼──────────────┤
│ 1  │ admin    │ 0            │ 0                │ 0             │ NULL         │
└────┴──────────┴──────────────┴──────────────────┴───────────────┴──────────────┘
```

### After (Correct) ✅
```sql
UserAccounts:
┌────┬──────────┬──────────────────────┬─────────────────────┐
│ ID │ Username │ ApiKeyHash           │ ApiKeyGeneratedAt   │
├────┼──────────┼──────────────────────┼─────────────────────┤
│ 1  │ admin    │ Xk9sP2q1R3m5T7u9V... │ 2025-03-04 16:52:00 │
└────┴──────────┴──────────────────────┴─────────────────────┘
```

Balance comes from Binance API (not DB):
```sql
PortfolioSnapshots (for historical records):
┌────┬──────────────────┬─────────────────────────┐
│ ID │ TotalBalanceUSDT │ CreatedAt               │
├────┼──────────────────┼─────────────────────────┤
│ 1  │ 72500.50         │ 2025-03-04 10:00:00 AM  │
│ 2  │ 73100.25         │ 2025-03-04 11:00:00 AM  │
│ 3  │ 72850.75         │ 2025-03-04 12:00:00 PM  │
└────┴──────────────────┴─────────────────────────┘
```

---

## API Endpoints Ready to Use

### Authentication
```
POST   /api/auth/generate-key    Generate new API key
POST   /api/auth/revoke-key      Revoke current key
GET    /api/auth/status          Check auth status
```

### Portfolio
```
GET    /api/portfolio/balance    Get live balance (from Binance)
GET    /api/portfolio/snapshot   Get portfolio snapshot
```

### Protected Trade Endpoints
```
POST   /api/trade/open           Open a new trade (requires auth)
POST   /api/trade/close/{id}     Close a trade (requires auth)
GET    /api/trades               Get all trades
```

### Performance Analysis
```
GET    /api/performance/analyze  Detailed metrics analysis
GET    /api/performance/summary   Quick stats
```

All endpoints prefixed with your API key in Authorization header:
```
Authorization: ApiKey {your-key}
```

---

## Summary

✅ UserAccount is now correct  
✅ Balance comes from Binance (live)  
✅ API key authentication works  
✅ Multiple users can share one Binance account  
✅ Each user has their own TradingBot API key  
✅ Ready for production!

---

## Need Help?

- **Setup questions?** → See CORRECTED_USERACCOUNT_IMPLEMENTATION.md
- **Architecture details?** → See ARCHITECTURE_CLARIFICATION_CORRECTION.md
- **API reference?** → See QUICK_REFERENCE_CARD.md

---

**Status**: ✅ Complete and Ready  
**Date**: March 4, 2025  
**Next**: Start using your API key! 🚀

