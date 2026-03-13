/* ============================================================
   CONFIG.JS — API Configuration
   CryptoBot Dashboard v2
   ============================================================ */

const _rawApiBase = localStorage.getItem("tb_v2_api_base_url") || "https://localhost:5001/api";
const _cleanApiBase = _rawApiBase.replace(/\/+$/, "");
const _apiBase = _cleanApiBase.endsWith("/api") ? _cleanApiBase : `${_cleanApiBase}/api`;

const CONFIG = {
  /* ── API ── */
  API_BASE_URL: _apiBase,
  API_KEY: localStorage.getItem("tb_v2_api_key") || "",
  WS_URL: "wss://localhost:5001/ws",

  /* ── Polling Intervals (ms) ── */
  POLL_FAST:   5000,   // Active trades, PnL
  POLL_MID:    10000,  // Orders, signals
  POLL_SLOW:   20000,  // Account overview, risk
  POLL_LOGS:   8000,   // Logs console

  /* ── Chart ── */
  DEFAULT_SYMBOL:    "BTCUSDT",
  DEFAULT_TIMEFRAME: "1h",
  CHART_CANDLE_LIMIT: 200,

  /* ── Symbols ── */
  WATCH_SYMBOLS: [],

  /* ── UI ── */
  MAX_LOG_ENTRIES:    300,
  MAX_SIGNAL_ENTRIES: 50,
  TOAST_DURATION:     4000,
  DATE_LOCALE:        "en-US",
  USE_PUBLIC_MARKET_DATA: true,

  /* ── Endpoints ── */
  ENDPOINTS: {
    // System
    systemStatus:       "/system/health",
    workerStatus:       "/system/worker-status",
    systemLogs:         "/system/logs",

    // Account
    accountBalance:     "/portfolio/balance",
    accountHoldings:    "/portfolio/holdings",
    portfolioDailyPnl:  "/portfolio/daily-pnl?days=30",
    tradeSummary:       "/trades/summary",

    // Trades
    tradesActive:       "/trades/open",
    tradesHistory:      "/trades?status=Closed&page=1&pageSize=100&sortBy=entryTime&desc=true",
    trades:             (query) => `/trades${query ? `?${query}` : ""}`,
    tradeById:          (id) => `/trades/${id}`,
    tradeClose:         (id) => `/trade/close/${id}`,

    // Orders
    orders:             "/trades?status=Closed&page=1&pageSize=40&sortBy=entryTime&desc=true",

    // AI
    aiVerifications:    "/ai/responses/latest",
    aiStatus:           "/ai/status",

    // Strategy
    strategyMode:       "/strategy/mode",
    strategyCustom:     "/strategy/custom",
    strategyActivate:   (id) => `/strategy/custom/${id}/activate`,
    strategyDeactivate: (id) => `/strategy/custom/${id}/deactivate`,

    // Risk
    riskProfile:        "/risk/profile",
    riskUpdate:         "/risk/profile",

    // Signals
    signals:            "/strategy/signals?count=50",

    // Market
    marketPrice:        (sym) => `/market/price/${sym}`,
    marketStatistics:   (sym) => `/market/statistics/${sym}`,
    marketCandles:      (sym, tf) => `/market/candles?symbol=${sym}&interval=${tf}&limit=${CONFIG.CHART_CANDLE_LIMIT}`,
    marketPairsActive:  "/market/pairs/active",
  }
};

/* ── Runtime Settings Store ── */
const AppState = {
  botRunning:      false,
  selectedSymbol:  CONFIG.DEFAULT_SYMBOL,
  selectedTf:      CONFIG.DEFAULT_TIMEFRAME,
  sidebarCollapsed: false,
  activeView:       "overview",
  activeTrades:     [],
  tradeHistory:     [],
  orders:           [],
  signals:          [],
  logs:             [],
  account:          null,
  riskProfile:      null,
  aiData:           [],
  systemStatus:     null,
  lastRefresh:      {},
  intervals:        {},
};

/* Allow runtime config update */
function updateConfig(overrides) {
  Object.assign(CONFIG, overrides);
}

window.CONFIG    = CONFIG;
window.AppState  = AppState;
window.updateConfig = updateConfig;
