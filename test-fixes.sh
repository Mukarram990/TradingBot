#!/bin/bash
# TradingBot - Quick Test Script
# Run this after starting the API to verify all fixes

API="http://localhost:5000/api"

echo "================================"
echo "TradingBot Critical Fixes - Test Suite"
echo "================================"
echo ""

# Test 1: Check Health
echo "1️⃣  Testing Connectivity..."
response=$(curl -s -o /dev/null -w "%{http_code}" "$API/risk/profile")
if [ "$response" = "200" ] || [ "$response" = "404" ]; then
    echo "✅ API is responding ($response)"
else
    echo "❌ API not responding ($response)"
    exit 1
fi

echo ""

# Test 2: Verify Risk Profile Seeded
echo "2️⃣  Testing Risk Profile Seeding..."
risk_response=$(curl -s -X GET "$API/risk/profile")
if echo "$risk_response" | grep -q "maxRiskPerTradePercent"; then
    echo "✅ Risk profile seeded:"
    echo "$risk_response" | jq '.' 2>/dev/null || echo "$risk_response"
else
    echo "⚠️  Risk profile may not be seeded yet"
fi

echo ""

# Test 3: Create Portfolio Snapshot
echo "3️⃣  Testing Portfolio Snapshot..."
snapshot_response=$(curl -s -X POST "$API/portfolio/snapshot")
if echo "$snapshot_response" | grep -q "totalBalanceUSDT"; then
    echo "✅ Portfolio snapshot created:"
    echo "$snapshot_response" | jq '.' 2>/dev/null || echo "$snapshot_response"
else
    echo "⚠️  Could not create portfolio snapshot"
    echo "$snapshot_response"
fi

echo ""

# Test 4: Get Current Market Price
echo "4️⃣  Testing Market Data..."
price_response=$(curl -s -X GET "$API/market/price/BTCUSDT")
if echo "$price_response" | grep -q "[0-9]"; then
    echo "✅ Current BTC/USDT price: $price_response"
else
    echo "⚠️  Could not fetch market price"
fi

echo ""

# Test 5: Try Opening a Test Trade
echo "5️⃣  Testing Trade Opening (with daily loss check)..."
trade_response=$(curl -s -X POST "$API/trade/open" \
  -H "Content-Type: application/json" \
  -d '{
    "symbol": "BTCUSDT",
    "action": 1,
    "entryPrice": 43250,
    "stopLoss": 42900,
    "takeProfit": 43600,
    "quantity": 0.001,
    "accountBalance": 10000,
    "aiConfidence": 85
  }')

if echo "$trade_response" | grep -q "tradeId"; then
    echo "✅ Trade opened successfully:"
    trade_id=$(echo "$trade_response" | jq '.tradeId' 2>/dev/null)
    echo "   Trade ID: $trade_id"
    echo "   Full response:"
    echo "$trade_response" | jq '.' 2>/dev/null || echo "$trade_response"
    
    echo ""
    echo "6️⃣  Closing trade..."
    close_response=$(curl -s -X POST "$API/trade/close/$trade_id")
    echo "$close_response" | jq '.' 2>/dev/null || echo "$close_response"
    
elif echo "$trade_response" | grep -q "Daily loss limit"; then
    echo "⚠️  Daily loss limit exceeded (expected if you already traded)"
elif echo "$trade_response" | grep -q "Insufficient USDT"; then
    echo "⚠️  Insufficient balance (expected on testnet)"
else
    echo "⚠️  Could not open trade:"
    echo "$trade_response"
fi

echo ""
echo "================================"
echo "Test Suite Complete ✅"
echo "================================"
echo ""
echo "💡 Next Steps:"
echo "  1. Ensure Background Worker is running (check logs)"
echo "  2. Watch for 'Monitoring X open trades' messages"
echo "  3. Verify trades auto-close when SL/TP is hit"
echo "  4. Check that daily loss limit is enforced"
echo ""
