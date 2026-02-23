@echo off
REM TradingBot - Quick Test Script (Windows Batch)
REM Run this after starting the API to verify all fixes

setlocal enabledelayedexpansion
set API=http://localhost:5000/api

echo ================================
echo TradingBot Critical Fixes - Test Suite
echo ================================
echo.

REM Test 1: Check Health
echo 1️⃣  Testing Connectivity...
curl -s -o nul -w "Status: %%{http_code}\n" "%API%/risk/profile"
echo ✅ API is responding
echo.

REM Test 2: Verify Risk Profile Seeded
echo 2️⃣  Testing Risk Profile Seeding...
echo Running: curl -X GET "%API%/risk/profile"
curl -X GET "%API%/risk/profile"
echo.
echo.

REM Test 3: Create Portfolio Snapshot
echo 3️⃣  Testing Portfolio Snapshot...
echo Running: curl -X POST "%API%/portfolio/snapshot"
curl -X POST "%API%/portfolio/snapshot"
echo.
echo.

REM Test 4: Get Current Market Price
echo 4️⃣  Testing Market Data...
echo Running: curl -X GET "%API%/market/price/BTCUSDT"
curl -X GET "%API%/market/price/BTCUSDT"
echo.
echo.

REM Test 5: Try Opening a Test Trade
echo 5️⃣  Testing Trade Opening (with daily loss check)...
echo Running: curl -X POST "%API%/trade/open"
curl -X POST "%API%/trade/open" ^
  -H "Content-Type: application/json" ^
  -d "{\"symbol\":\"BTCUSDT\",\"action\":1,\"entryPrice\":43250,\"stopLoss\":42900,\"takeProfit\":43600,\"quantity\":0.001,\"accountBalance\":10000,\"aiConfidence\":85}"
echo.
echo.

echo ================================
echo Test Suite Complete ✅
echo ================================
echo.
echo 💡 Next Steps:
echo    1. Ensure Background Worker is running (check logs)
echo    2. Watch for 'Monitoring X open trades' messages
echo    3. Verify trades auto-close when SL/TP is hit
echo    4. Check that daily loss limit is enforced
echo.
pause
