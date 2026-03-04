# 🚀 TradingBot - Step-by-Step Implementation Guide

## ✅ PREREQUISITES COMPLETED

- ✅ All 5 critical fixes implemented
- ✅ Project builds successfully  
- ✅ Changed from GUID to int (auto-increment)
- ✅ Background worker registered
- ✅ Risk enforcement added
- ✅ Daily loss limit check added
- ✅ SL/TP auto-close service created

---

## 📋 STEP-BY-STEP IMPLEMENTATION

### **STEP 1: Install EF Core Tools** (5 minutes)

#### Option A: Global Installation (Recommended)
```bash
dotnet tool install --global dotnet-ef
```

Verify:
```bash
dotnet ef --version
```

#### Option B: Local Project Installation
```bash
cd D:\Personal\TradingBot
dotnet tool install dotnet-ef
dotnet tool restore
```

---

### **STEP 2: Create Database Migration** (2 minutes)

```bash
cd D:\Personal\TradingBot

# Create migration for critical fixes
dotnet ef migrations add AddCriticalFixes -p TradingBot.Persistence -s TradingBot

# List migrations to verify
dotnet ef migrations list -p TradingBot.Persistence
```

Expected output:
```
Build started...
Build succeeded.

The Entity Framework operations completed successfully.
```

---

### **STEP 3: Apply Migration to Database** (2 minutes)

```bash
# Update database with new migration
dotnet ef database update -p TradingBot.Persistence -s TradingBot
```

Expected output:
```
Build started...
Build succeeded.

Applying migration '20250223_AddCriticalFixes'.
Done.
```

✅ **Your database is now updated!**

---

### **STEP 4: Configure API Keys (USER-SECRETS)** (5 minutes)

#### Step 4A: Initialize User Secrets
```bash
cd D:\Personal\TradingBot\TradingBot

# One-time initialization
dotnet user-secrets init
```

This creates a secrets file locally on your machine at:
```
%APPDATA%\Microsoft\UserSecrets\<user-secret-id>\secrets.json
```

#### Step 4B: Add Your Binance Testnet Credentials

Go to https://testnet.binance.vision and get your API credentials, then run:

```bash
# Store API Key
dotnet user-secrets set "Binance:ApiKey" "your-testnet-api-key-here"

# Store API Secret
dotnet user-secrets set "Binance:ApiSecret" "your-testnet-api-secret-here"
```

#### Step 4C: Verify Secrets Are Set
```bash
dotnet user-secrets list
```

You should see:
```
Binance:ApiKey = ***
Binance:ApiSecret = ***
```

✅ **Your credentials are now secure (not in code or git)!**

---

### **STEP 5: Build the Project** (2 minutes)

```bash
cd D:\Personal\TradingBot

# Clean build
dotnet clean

# Rebuild everything
dotnet build

# Should see:
# Build succeeded.
```

If build fails:
- Check C# compiler version (`dotnet --version`)
- Clear obj/bin folders: `dotnet clean`
- Restore NuGet packages: `dotnet restore`

---

### **STEP 6: Start the Application** (1 minute)

```bash
cd D:\Personal\TradingBot

dotnet run --project TradingBot
```

Expected console output:
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:7123
      
info: TradingBot.Workers.TradeMonitoringWorker[0]
      Trade Monitoring Worker started
      
Application started. Press Ctrl+C to stop.
```

**✅ Application is running!** Keep this terminal open.

---

### **STEP 7: Test the Fixes** (10 minutes)

#### Option A: Using PowerShell (Windows)

Run these commands in a **NEW PowerShell terminal**:

```powershell
$API = "http://localhost:5000/api"

# Test 1: Risk Profile
Write-Host "Test 1: Risk Profile"
Invoke-RestMethod -Uri "$API/risk/profile" | ConvertTo-Json

# Test 2: Portfolio Snapshot
Write-Host "`nTest 2: Create Portfolio Snapshot"
Invoke-RestMethod -Method Post -Uri "$API/portfolio/snapshot" | ConvertTo-Json

# Test 3: Get Market Price
Write-Host "`nTest 3: Current BTC Price"
Invoke-RestMethod -Uri "$API/market/price/BTCUSDT"

# Test 4: Open Trade
Write-Host "`nTest 4: Open Trade"
$tradeBody = @{
    symbol = "BTCUSDT"
    action = 1
    entryPrice = 43250
    stopLoss = 42900
    takeProfit = 43600
    quantity = 0.001
    accountBalance = 10000
    aiConfidence = 85
} | ConvertTo-Json

$trade = Invoke-RestMethod -Method Post -Uri "$API/trade/open" `
    -ContentType "application/json" -Body $tradeBody
$trade | ConvertTo-Json
$tradeId = $trade.tradeId

# Test 5: Close Trade
Write-Host "`nTest 5: Close Trade"
Invoke-RestMethod -Method Post -Uri "$API/trade/close/$tradeId" | ConvertTo-Json
```

#### Option B: Using Curl (Windows Command Prompt)

Run `test-fixes.bat` in the project root:
```bash
test-fixes.bat
```

Or manually:
```cmd
cd D:\Personal\TradingBot

curl -X GET http://localhost:5000/api/risk/profile

curl -X POST http://localhost:5000/api/portfolio/snapshot

curl -X GET http://localhost:5000/api/market/price/BTCUSDT

curl -X POST http://localhost:5000/api/trade/open ^
  -H "Content-Type: application/json" ^
  -d "{\"symbol\":\"BTCUSDT\",\"action\":1,\"entryPrice\":43250,\"stopLoss\":42900,\"takeProfit\":43600,\"quantity\":0.001,\"accountBalance\":10000,\"aiConfidence\":85}"
```

#### Option C: Using Postman/Swagger UI

1. Open browser: `http://localhost:5000`
2. You'll see Swagger UI (auto-generated API docs)
3. Click on endpoints to test them
4. All requests and responses will be visible

---

### **STEP 8: Verify Background Worker** (5 minutes)

While the app is running, monitor these logs:

#### Every 10 seconds, you should see:
```
info: TradingBot.Infrastructure.Services.TradeMonitoringService[0]
      Monitoring 0 open trades...
```

#### When trades are open:
```
info: TradingBot.Infrastructure.Services.TradeMonitoringService[0]
      Monitoring 1 open trades...
```

#### When SL/TP is triggered:
```
warn: TradingBot.Infrastructure.Services.TradeMonitoringService[0]
      SL hit for BTCUSDT at 42800.00. Closing trade.
```

---

### **STEP 9: Verify Risk Enforcement** (5 minutes)

#### Test Daily Loss Limit:

1. **Create snapshot (captures starting balance):**
```bash
POST http://localhost:5000/api/portfolio/snapshot
```

2. **Simulate a losing trade** (manually subtract from balance in DB if needed)

3. **Try to open another trade:**
```bash
POST http://localhost:5000/api/trade/open
```

If you've lost more than 5%, you should see:
```json
{
  "error": "Daily loss limit exceeded. Trading halted."
}
```

✅ **Daily loss limit is enforced!**

---

## 📊 VERIFICATION CHECKLIST

Before moving to the next phase, verify all of these:

### Database
- [ ] Migration applied without errors
- [ ] Tables created successfully
- [ ] RiskProfile table has 1 default row
- [ ] BaseEntity table uses int identity
- [ ] Order table has int TradeId FK

### Application
- [ ] Builds without errors
- [ ] Starts without exceptions
- [ ] Background worker starts automatically
- [ ] Logs show "Trade Monitoring Worker started"

### API Endpoints  
- [ ] GET /api/risk/profile returns data
- [ ] POST /api/portfolio/snapshot creates snapshot
- [ ] GET /api/market/price/{symbol} returns price
- [ ] POST /api/trade/open opens trade
- [ ] POST /api/trade/close/{id} closes trade
- [ ] PUT /api/risk/profile updates settings

### Risk Management
- [ ] Daily loss limit check runs before opening trade
- [ ] Background worker monitors open trades every 10 seconds
- [ ] Trades auto-close on SL/TP hit
- [ ] Position size calculated using 2% rule
- [ ] Circuit breaker works (stops after 3 losses)
- [ ] Max trades per day enforced (5 trades limit)

### Security
- [ ] API keys NOT in appsettings.json
- [ ] API keys NOT in code/comments
- [ ] API keys loaded from user-secrets
- [ ] .gitignore has secrets patterns

---

## 🐛 TROUBLESHOOTING

### Issue: "dotnet ef" command not found
**Solution**: Install globally:
```bash
dotnet tool install --global dotnet-ef
```

---

### Issue: Migration fails with "snapshot already exists"
**Solution**: Delete the problematic migration:
```bash
dotnet ef migrations remove -p TradingBot.Persistence
```

---

### Issue: Database update fails
**Solution**: Check connection string in appsettings.json:
```bash
# Make sure MSSQL is running and connection is correct
# Test connection with SQL Server Management Studio
```

---

### Issue: Application won't start - "Binance:ApiKey not found"
**Solution**: Set user-secrets:
```bash
cd TradingBot
dotnet user-secrets set "Binance:ApiKey" "your-key"
dotnet user-secrets set "Binance:ApiSecret" "your-secret"
```

---

### Issue: Background worker not starting
**Solution**: Check Program.cs:
```csharp
builder.Services.AddHostedService<TradeMonitoringWorker>();
```
Should be registered. If missing, add it.

---

### Issue: Trades don't auto-close
**Solution**: 
1. Check background worker is running (check logs)
2. Verify trade is in OPEN status
3. Check current price using `/api/market/price/{symbol}`
4. Verify price is actually hitting SL/TP levels
5. Check Binance API connectivity

---

## 📈 NEXT PHASE (After Verification)

Once all fixes are verified working:

### Phase 2: Automation Layer (Weeks 2-3)
- [ ] Indicator computation service (RSI, EMA, MACD)
- [ ] Strategy engine for signal generation
- [ ] Scheduled signal generation every 5 minutes
- [ ] Performance analytics calculations

### Phase 3: Intelligence Layer (Weeks 4-5)
- [ ] Gemini AI integration
- [ ] AI signal validation
- [ ] Auto-trade from signals
- [ ] Market regime detection

### Phase 4: Production Hardening (Week 6)
- [ ] Rate limiting
- [ ] Retry policies
- [ ] Health checks
- [ ] Load testing

---

## 🎉 SUCCESS!

If you've completed all steps and verified everything:

✅ **Your trading bot is now:**
- Secure (credentials managed)
- Automated (background monitoring)
- Risk-managed (daily loss limits)
- Auto-closing (SL/TP triggers)
- Configurable (API-driven settings)

**Ready for:** Next phase of development or limited live trading on testnet!

---

## 📞 QUICK REFERENCE COMMANDS

```bash
# Build
dotnet build

# Run
dotnet run --project TradingBot

# Create migration
dotnet ef migrations add <name> -p TradingBot.Persistence -s TradingBot

# Update database
dotnet ef database update -p TradingBot.Persistence -s TradingBot

# List migrations
dotnet ef migrations list -p TradingBot.Persistence

# Set user secret
dotnet user-secrets set "Key" "Value"

# List user secrets
dotnet user-secrets list

# Remove user secret
dotnet user-secrets remove "Key"

# Clean
dotnet clean

# Restore
dotnet restore

# Test API
curl -X GET http://localhost:5000/api/risk/profile
```

---

**Status**: ✅ All Critical Fixes Implemented  
**Build**: ✅ Successful  
**Ready for**: Database Migration + Testing  
**Estimated Time**: 30-45 minutes to complete all steps
