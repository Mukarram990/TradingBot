# ⚠️ CORRECTED: UserAccount Setup for API Authentication

## Important Clarification

**The UserAccount balance fields (TotalBalance, AvailableBalance, LockedBalance) should NOT be manually set.**

Your bot architecture is:
- **ONE Binance API key** (in User Secrets)
- **ONE Binance account** being traded
- **Balance fetched LIVE from Binance** (not stored in DB)
- **UserAccount** is for dashboard user management ONLY

---

## Correct UserAccount Setup

### Step 1: Create Dashboard User (No Balance)
```sql
-- Create a dashboard user for API authentication ONLY
INSERT INTO UserAccounts (Username, IsActive, CreatedAt)
VALUES ('admin', 1, GETUTCDATE());
```

**That's it!** Don't add balance values - they come from Binance dynamically.

### Step 2: Generate TradingBot API Key
```bash
curl -X POST "http://localhost:5000/api/auth/generate-key" \
  -H "Authorization: ApiKey admin" \
  -H "Content-Type: application/json"

# Response:
{
  "apiKey": "aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==",
  "warning": "Save this key securely!"
}
```

### Step 3: Use Key to Access TradingBot API
```bash
curl -H "Authorization: ApiKey aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==" \
  http://localhost:5000/api/portfolio/balance

# Response (fetched LIVE from Binance):
{
  "totalBalanceUSDT": 72500.50,
  "fetchedAt": "2025-02-23T18:35:00Z",
  "source": "Binance API (Live)"
}
```

---

## How Balance Actually Works

### ✅ CORRECT Flow

```
Binance API Key (User Secrets)
        ↓
BinanceAccountService.GetAccountInfoAsync()
        ↓
Convert all assets to USDT value:
├─ USDT balance: 50,000
├─ BTC balance: 0.5 BTC = 22,500 USDT
└─ Total: 72,500 USDT
        ↓
Return to Dashboard (displayed in real-time)
        ↓
Optional: CreateSnapshot for historical record
(stored in PortfolioSnapshots table)
```

### ❌ WRONG Approach

```
Don't do this:
INSERT INTO UserAccounts 
(Username, TotalBalance, AvailableBalance)
VALUES ('admin', 50000, 50000);
```

This is hardcoded and never updates!

---

## Important Distinction: Two Different API Keys

### 1️⃣ **Binance API Key** (Secret, Single)
```json
{
  "apiKey": "abc123xyz789...",
  "apiSecret": "secret789xyz123...",
  "stored": "appsettings.json or User Secrets",
  "used_for": "Authenticate with Binance servers",
  "count": "ONE key - the bot's trading account"
}
```

### 2️⃣ **TradingBot API Key** (Per User, Multiple)
```json
{
  "apiKey": "aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==",
  "stored": "UserAccount.ApiKeyHash (hashed)",
  "used_for": "Authenticate with TradingBot API endpoints",
  "count": "Multiple - one per dashboard user"
}
```

**In Headers:**
```
Binance API calls:
  X-MBX-APIKEY: abc123xyz789...

TradingBot API calls:
  Authorization: ApiKey aB+cDeF/gHiJkLmNoPqRsTuVwXyZ0123456789ABC==
```

---

## Correct Initialization Sequence

### Step 1: Deploy Application
```bash
dotnet run --project TradingBot
```

### Step 2: Apply Migration
```bash
dotnet ef database update --project TradingBot.Persistence --startup-project TradingBot
```

### Step 3: Create Dashboard User (Only)
```bash
# Via SQL
INSERT INTO UserAccounts (Username, IsActive, CreatedAt)
VALUES ('dashboard-user', 1, GETUTCDATE());

# OR via endpoint (if bootstrap code exists)
```

### Step 4: Generate API Key
```bash
curl -X POST "http://localhost:5000/api/auth/generate-key" \
  -H "Authorization: ApiKey dashboard-user"
  
# Save returned key!
```

### Step 5: Use It
```bash
# Check auth status
curl "http://localhost:5000/api/auth/status" \
  -H "Authorization: ApiKey {your-key}"

# Get live balance from Binance
curl "http://localhost:5000/api/portfolio/balance" \
  -H "Authorization: ApiKey {your-key}"

# Get trade history
curl "http://localhost:5000/api/trades" \
  -H "Authorization: ApiKey {your-key}"

# All balance data comes from Binance, not DB
```

---

## What NOT to Do

❌ **Don't manually set balance in DB**
```sql
-- WRONG
UPDATE UserAccounts 
SET TotalBalance = 50000, AvailableBalance = 50000
WHERE ID = 1;
```

❌ **Don't create multiple Binance accounts**
```sql
-- WRONG
INSERT INTO UserAccounts (Username, Binance_ApiKey, Binance_ApiSecret)
VALUES ('trader1', 'key1', 'secret1');
INSERT INTO UserAccounts (Username, Binance_ApiKey, Binance_ApiSecret)
VALUES ('trader2', 'key2', 'secret2');

-- Bot only uses ONE key from User Secrets!
```

❌ **Don't assume balance fields are used**
```csharp
// WRONG
var balance = userAccount.TotalBalance;  // This is stale/unused
var available = userAccount.AvailableBalance;  // This is stale/unused

// CORRECT
var account = await _binanceService.GetAccountInfoAsync();  // Live from Binance
```

---

## Where Balance Actually Comes From

### In Code
```csharp
// Controllers/PortfolioController.cs
[HttpGet("balance")]
public async Task<IActionResult> GetBalance()
{
    // THIS is where balance comes from:
    var account = await _accountService.GetAccountInfoAsync();
    
    decimal totalUsdtValue = 0m;
    foreach (var balance in account.Balances)
    {
        if (balance.Asset == "USDT")
        {
            totalUsdtValue += decimal.Parse(balance.Free);
        }
        else
        {
            var price = await _marketService.GetCurrentPriceAsync(balance.Asset + "USDT");
            totalUsdtValue += decimal.Parse(balance.Free) * price;
        }
    }
    
    return Ok(new { totalUsdtValue, fetchedAt = DateTime.UtcNow });
}
```

### In Database
```sql
-- PortfolioSnapshots table stores HISTORICAL snapshots
-- NOT live balance
SELECT * FROM PortfolioSnapshots
ORDER BY CreatedAt DESC
LIMIT 10;

-- Shows how balance changed over time
-- Example output:
-- ID | TotalBalanceUSDT | CreatedAt
-- 1  | 73,100          | 2025-02-23 12:00 PM
-- 2  | 72,800          | 2025-02-23 11:00 AM
-- 3  | 72,500          | 2025-02-23 10:00 AM
```

---

## Summary Table

| Question | Answer |
|----------|--------|
| Where does balance come from? | **Binance API (live)** - NOT UserAccount table |
| What is UserAccount.TotalBalance for? | **NOT USED** - should be removed or marked obsolete |
| How many Binance accounts? | **ONE** - bot trades on single account |
| How many Binance API keys? | **ONE** - stored in User Secrets |
| How many TradingBot API keys? | **Multiple** - one per dashboard user |
| How is balance updated? | **Automatically** on every /api/portfolio/balance request |
| How is history stored? | **PortfolioSnapshots table** - periodic snapshots |

---

## Corrected Setup Checklist

- [ ] Migration applied (`dotnet ef database update`)
- [ ] ONE dashboard user created in UserAccounts table
- [ ] **No balance values manually inserted**
- [ ] API key generated via `/api/auth/generate-key`
- [ ] Key tested with `/api/portfolio/balance`
- [ ] Confirm balance returns LIVE value from Binance
- [ ] NOT returning hardcoded DB value
- [ ] Multiple users can share same Binance account
- [ ] Each user has their own TradingBot API key

---

## If You See This Problem

**Symptom:** Balance doesn't change even though bot traded

**Cause:** Probably checking `UserAccount.TotalBalance` which is stale

**Fix:** Use `BinanceAccountService.GetAccountInfoAsync()` instead

```csharp
// WRONG
var balance = userAccount.TotalBalance;  // Stale!

// CORRECT
var account = await _accountService.GetAccountInfoAsync();
var balance = account.Balances.FirstOrDefault(b => b.Asset == "USDT")?.Free;
```

---

**Correction Version**: 1.0  
**Date**: February 23, 2025  
**Status**: Clarified and Fixed

