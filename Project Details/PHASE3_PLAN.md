# PHASE 3 — AI INTELLIGENCE LAYER

## ✅ STATUS: IMPLEMENTED

---

## 🎯 PHASE 3 GOALS

| Goal | Status |
|------|--------|
| Multi-model AI validation of trade signals | ✅ DONE |
| Intelligent provider rotation on rate limits | ✅ DONE |
| Market regime detection (Trending/Ranging/Volatile/Bearish) | ✅ DONE |
| AI responses persisted to AIResponses table | ✅ DONE |
| Dashboard API endpoints for AI status | ✅ DONE |
| Graceful degradation (trade skipped, not crashed) | ✅ DONE |
| User-secrets for all API keys | ✅ DONE |

---

## 🤖 SUPPORTED AI MODELS (ALL FREE TIER)

| Provider | Model | Free Quota | Strength |
|----------|-------|-----------|----------|
| **Google Gemini** | `gemini-2.0-flash` | 15 req/min | Best reasoning, JSON mode |
| **Groq** | `llama-3.1-70b-versatile` | 30 req/min, 14k/day | Ultra-fast, strong |
| **OpenRouter** | `meta-llama/llama-3.1-8b-instruct:free` | Free tier | No CC required |
| **Cohere** | `command-r` | 1,000 req/month | Good instruction following |

**Default priority:** Gemini → Groq → OpenRouter → Cohere  
Configurable via `AI:ProviderPriority` in appsettings.json.

---

## 🔑 API KEY SETUP (user-secrets — never appsettings.json)

```bash
cd TradingBot

# Initialize user-secrets (only needed once)
dotnet user-secrets init

# ── Binance (already done in Phase 1) ──
dotnet user-secrets set "Binance:ApiKey"     "your-binance-testnet-key"
dotnet user-secrets set "Binance:ApiSecret"  "your-binance-testnet-secret"

# ── Gemini ──
# Get key: https://aistudio.google.com/apikey (free, no CC)
dotnet user-secrets set "AI:GeminiApiKey" "AIza..."

# ── Groq ──
# Get key: https://console.groq.com/keys (free, no CC)
dotnet user-secrets set "AI:GroqApiKey" "gsk_..."

# ── OpenRouter ──
# Get key: https://openrouter.ai/keys (free tier, no CC for :free models)
dotnet user-secrets set "AI:OpenRouterApiKey" "sk-or-..."

# ── Cohere ──
# Get key: https://dashboard.cohere.com/api-keys (free trial, 1k calls/month)
dotnet user-secrets set "AI:CohereApiKey" "..."

# Verify all secrets are set
dotnet user-secrets list
```

**Note:** You can configure only 1 key if you prefer. The multi-provider engine
skips providers with missing keys (logs a debug message, tries the next one).
Minimum viable config: just Gemini or just Groq.

---

## 🏗️ ARCHITECTURE

```
SignalGenerationWorker (every 5 min)
    │
    ├─ IMarketScannerService.ScanAllPairsAsync()
    │      └─ Fetches candles → computes indicators → saves IndicatorSnapshots
    │
    └─ For each snapshot:
           │
           └─ AIEnhancedStrategyEngine.EvaluateWithAIAsync()
                  │
                  ├─ 1. IStrategyEngine.EvaluateSignal()      ← rule-based filter
                  │       └─ RSI + EMA + MACD confluence check
                  │
                  ├─ 2. MarketRegimeDetector.GetLatestRegimeAsync()
                  │       └─ Suppress BUY in Bearish / Volatile regimes
                  │
                  ├─ 3. MultiProviderAIService.ValidateSignalAsync()
                  │       │
                  │       ├─ Try Gemini      ← 429? → cooldown 90s, try next
                  │       ├─ Try Groq        ← 429? → cooldown 90s, try next
                  │       ├─ Try OpenRouter  ← 429? → cooldown 90s, try next
                  │       └─ Try Cohere      ← 429? → AiAllProvidersExhaustedException
                  │               └─ Worker skips trade for this symbol gracefully
                  │
                  ├─ 4. Persist to AIResponses table
                  │
                  └─ 5. confidence >= 70 AND action == BUY ?
                              YES → OpenTradeAsync()
                              NO  → skip, log reason
```

---

## 📁 NEW FILES

### TradingBot.Domain/
```
Models/
  AISignalResult.cs          ← Structured AI response model
Enums/
  AiProviderType.cs          ← Gemini | Groq | OpenRouter | Cohere enum
Interfaces/
  IAISignalService.cs        ← Contract for all AI providers
```

### TradingBot.Infrastructure/AI/
```
AiOptions.cs                 ← Configuration POCO (keys, models, priority)
AiRateLimitException.cs      ← AiRateLimitException + AiAllProvidersExhaustedException
GeminiAIService.cs           ← Google Gemini provider
GroqAIService.cs             ← Groq provider (OpenAI-compatible)
OpenRouterAIService.cs       ← OpenRouter free-model aggregator
CohereAIService.cs           ← Cohere v2 Chat API
MultiProviderAIService.cs    ← Orchestrator with rotation + cooldown tracking
MarketRegimeDetector.cs      ← AI + rule-based regime classification
AIEnhancedStrategyEngine.cs  ← Full pipeline (rules → regime → AI → execute)
```

### TradingBot/ (API project)
```
Controllers/
  AIController.cs            ← /api/ai/* endpoints for dashboard
Workers/
  SignalGenerationWorker.cs  ← UPDATED: uses AIEnhancedStrategyEngine
Program.cs                   ← UPDATED: registers all Phase 3 services
appsettings.json             ← UPDATED: AI section with model names
```

---

## 🌐 NEW API ENDPOINTS

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`  | `/api/ai/status` | Provider health + cooldown timers |
| `GET`  | `/api/ai/responses` | Paginated AI decision history |
| `GET`  | `/api/ai/responses/latest` | Latest AI decision per symbol |
| `POST` | `/api/ai/validate/{symbol}` | Manually trigger AI validation |
| `GET`  | `/api/ai/regimes` | Current market regime per pair |
| `POST` | `/api/ai/regime/{symbol}` | Re-detect regime for a symbol |

---

## 🔄 INTELLIGENT ROTATION LOGIC

```
Providers tried in priority order.

On AiRateLimitException (HTTP 429):
  → Mark provider as "cooling down" for RateLimitBackoffSeconds (default: 90s)
  → Try next provider immediately

On missing API key (InvalidOperationException):
  → Skip silently (debug log), try next provider

On unexpected error:
  → Log as error, try next provider

On AiAllProvidersExhaustedException (all exhausted):
  → SignalGenerationWorker skips trade for this symbol
  → Logs warning to SystemLogs table
  → Continues to next symbol
  → Next 5-minute tick: providers may have recovered from cooldown

Cooldown state is IN-MEMORY only (resets on app restart).
This is intentional — rate limit windows are 60-90s, app restart is rare.
```

---

## ✅ VERIFICATION CHECKLIST

After pushing and running:

```sql
-- 1. AI responses being saved?
SELECT TOP 10 Symbol, ParsedAction, Confidence, RawResponse, Timestamp
FROM AIResponses ORDER BY Timestamp DESC

-- 2. Market regimes being classified?
SELECT Symbol, Trend, Volatility, DetectedAt
FROM MarketRegimes ORDER BY DetectedAt DESC

-- 3. AI-approved signals?
SELECT * FROM TradeSignals ORDER BY CreatedAt DESC

-- 4. Worker activity?
SELECT TOP 20 Level, Message, CreatedAt
FROM SystemLogs
WHERE Message LIKE '%SignalGenerationWorker%'
ORDER BY CreatedAt DESC

-- 5. AI provider used per trade?
SELECT r.Symbol, r.ParsedAction, r.Confidence, r.RawResponse
FROM AIResponses r ORDER BY r.Timestamp DESC
```

API health check:
```bash
curl http://localhost:5000/api/ai/status
curl http://localhost:5000/api/ai/responses/latest
curl http://localhost:5000/api/ai/regimes
```

---

## 🔧 CONFIGURATION TUNING

Change AI settings in appsettings.json (no recompile needed):

```json
{
  "AI": {
    "MinConfidenceToTrade": 70,         // Raise to 80 for more conservative trading
    "RateLimitBackoffSeconds": 90,       // Lower to 60 if providers reset faster
    "ProviderPriority": ["Groq", "Gemini", "OpenRouter", "Cohere"]  // Change order
  }
}
```

Change model per provider:
```json
{
  "AI": {
    "GeminiModel":     "gemini-1.5-flash",              // Older, still free
    "GroqModel":       "mixtral-8x7b-32768",             // Alternative Groq model
    "OpenRouterModel": "mistralai/mistral-7b-instruct:free",  // Another free option
    "CohereModel":     "command-r-plus"                  // Stronger, uses more quota
  }
}
```

---

## 📊 PHASE COMPLETION SUMMARY

| Phase | Status | Description |
|-------|--------|-------------|
| **Phase 1** | ✅ COMPLETE | Security fixes, SL/TP worker, daily loss limit, risk DB |
| **Phase 2** | ✅ COMPLETE | Indicators, scanner, strategy engine, signal worker, read APIs |
| **Phase 3** | ✅ COMPLETE | Multi-model AI, rotation, market regime, AI dashboard APIs |
| **Phase 4** | 🔜 NEXT | Retry policies, rate limiting, health checks, production hardening |

---

*Generated: Phase 3 implementation complete*  
*Stack: .NET 10, C# 14, Entity Framework Core 10, SQL Server*  
*AI Providers: Google Gemini, Groq, OpenRouter, Cohere*
