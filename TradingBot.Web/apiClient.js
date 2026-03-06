/* ============================================================
   APICLIENT.JS — Reusable API Client
   CryptoBot Dashboard v2
   ============================================================ */

const ApiClient = (() => {
  const REQUEST_TIMEOUT_MS = 20000;
  const ERROR_LOG_DEDUPE_MS = 30000;
  const _errorLogGate = new Map();
  const _tickerCache = new Map();
  const _cache = new Map();

  function getApiKey() {
    return CONFIG.API_KEY || localStorage.getItem("tb_v2_api_key") || "";
  }

  function setApiKey(key) {
    CONFIG.API_KEY = key || "";
    localStorage.setItem("tb_v2_api_key", CONFIG.API_KEY);
  }

  function shouldLogError(key) {
    const now = Date.now();
    const last = _errorLogGate.get(key) || 0;
    if (now - last < ERROR_LOG_DEDUPE_MS) return false;
    _errorLogGate.set(key, now);
    return true;
  }

  async function getCached(key, ttlMs, loader) {
    const hit = _cache.get(key);
    const now = Date.now();
    if (hit && now - hit.ts < ttlMs) return hit.value;
    const value = await loader();
    _cache.set(key, { value, ts: now });
    return value;
  }

  async function request(endpoint, options = {}) {
    const url = `${CONFIG.API_BASE_URL}${endpoint}`;
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    const headers = {
      "X-API-KEY": getApiKey(),
      ...(options.body ? { "Content-Type": "application/json" } : {}),
      ...(options.headers || {})
    };

    try {
      const response = await fetch(url, { ...options, headers, signal: controller.signal });

      if (response.status === 401) throw Object.assign(new Error("Unauthorized"), { status: 401 });
      if (response.status === 429) throw Object.assign(new Error("Rate limited"), { status: 429 });

      if (!response.ok) {
        const errText = await response.text().catch(() => "Unknown error");
        throw Object.assign(new Error(`HTTP ${response.status}: ${errText}`), { status: response.status });
      }

      const contentType = response.headers.get("content-type") || "";
      return contentType.includes("application/json") ? await response.json() : await response.text();
    } catch (err) {
      if (err.name === "AbortError") {
        err = Object.assign(new Error("Request timeout"), { status: 408 });
      }
      const code = err?.status || "ERR";
      if (window.Logs && shouldLogError(`${endpoint}:${code}`)) {
        Logs.add(`API Error [${endpoint}]: ${err.message}`, "ERROR");
      }
      throw err;
    } finally {
      clearTimeout(timeout);
    }
  }

  const get = (ep, opts = {}) => request(ep, { method: "GET", ...opts });
  const post = (ep, body, opts = {}) => request(ep, { method: "POST", body: JSON.stringify(body ?? {}), ...opts });
  const put = (ep, body, opts = {}) => request(ep, { method: "PUT", body: JSON.stringify(body ?? {}), ...opts });
  const del = (ep, opts = {}) => request(ep, { method: "DELETE", ...opts });

  async function getBinanceCandles(symbol, interval, limit = 200) {
    try {
      const apiCandles = await get(CONFIG.ENDPOINTS.marketCandles(symbol, interval));
      return (apiCandles || []).map((k) => ({
        time: Math.floor(new Date(k.openTime).getTime() / 1000),
        open: Number(k.open),
        high: Number(k.high),
        low: Number(k.low),
        close: Number(k.close),
        volume: Number(k.volume)
      }));
    } catch {
      const url = `https://api.binance.com/api/v3/klines?symbol=${symbol}&interval=${interval}&limit=${limit}`;
      const resp = await fetch(url);
      const data = await resp.json();
      return data.map((k) => ({
        time: Math.floor(k[0] / 1000),
        open: parseFloat(k[1]),
        high: parseFloat(k[2]),
        low: parseFloat(k[3]),
        close: parseFloat(k[4]),
        volume: parseFloat(k[5])
      }));
    }
  }

  async function getBinanceAllTickers(symbols) {
    if (!symbols?.length) return {};

    const results = {};

    // Prefer one lightweight backend call over N heavy statistics calls.
    const joined = symbols.join(",");
    const bulk = await get(`/market/prices?symbols=${encodeURIComponent(joined)}`).catch(() => null);
    if (bulk?.data?.length) {
      bulk.data.forEach((row) => {
        if (!row?.symbol || row?.price == null) return;
        const price = Number(row.price);
        const prev = _tickerCache.get(row.symbol);
        const pct = prev && prev > 0 ? ((price - prev) / prev) * 100 : 0;
        _tickerCache.set(row.symbol, price);
        results[row.symbol] = {
          symbol: row.symbol,
          lastPrice: String(price),
          priceChangePercent: String(pct)
        };
      });
      return results;
    }

    // Fallback path: per-symbol statistics.
    await Promise.allSettled(symbols.map(async (symbol) => {
      const market = await get(
        CONFIG.ENDPOINTS.marketStatistics
          ? CONFIG.ENDPOINTS.marketStatistics(symbol)
          : `/market/statistics/${symbol}`
      ).catch(() => null);
      if (!market) return;
      results[symbol] = {
        symbol: market.symbol,
        lastPrice: String(market.currentPrice),
        priceChangePercent: String(market.priceChangePct ?? 0)
      };
    }));

    return results;
  }

  async function getBinanceTicker(symbol) {
    const map = await getBinanceAllTickers([symbol]);
    return map[symbol] || null;
  }

  async function fetchSystemStatus() {
    const [healthRes, workersRes, aiRes] = await Promise.allSettled([
      get(CONFIG.ENDPOINTS.systemStatus),
      get(CONFIG.ENDPOINTS.workerStatus),
      get("/ai/responses/latest")
    ]);

    const health = healthRes.status === "fulfilled" ? healthRes.value : {};
    const workers = workersRes.status === "fulfilled" ? workersRes.value : { workers: [] };
    const aiLatest = aiRes.status === "fulfilled" ? aiRes.value : { data: [] };

    const sig = (workers.workers || []).find((w) => w.name === "SignalGenerationWorker");
    const mon = (workers.workers || []).find((w) => w.name === "TradeMonitoringWorker");

    return {
      botStatus: sig?.status === "running" ? "Running" : "Idle",
      exchangeConnection: health?.status === "healthy" ? "Connected" : "Degraded",
      lastStrategyRun: sig?.lastSeen || null,
      lastAIVerification: aiLatest?.data?.[0]?.timestamp || null,
      totalSignalsGenerated: 0,
      uptime: health.uptimeFormatted || "-",
      version: "2.0.0",
      raw: { health, workers }
    };
  }

  async function fetchAccount() {
    const [balanceRes, summaryRes] = await Promise.allSettled([
      get(CONFIG.ENDPOINTS.accountBalance),
      get(CONFIG.ENDPOINTS.tradeSummary)
    ]);

    if (balanceRes.status === "rejected" && summaryRes.status === "rejected") {
      throw balanceRes.reason || summaryRes.reason;
    }

    const balance = balanceRes.status === "fulfilled" ? balanceRes.value : {};
    const summary = summaryRes.status === "fulfilled" ? summaryRes.value : {};

    return {
      totalBalance: Number(balance.totalUsdtValue || 0),
      availableBalance: Number(balance.totalUsdtValue || 0),
      totalPnL: Number(summary.netPnL || 0),
      dailyPnL: Number(balance.todayPnL || 0),
      winRate: Number(summary.winRate || 0),
      totalTrades: Number(summary.totalTrades || 0),
      activeTrades: Number(summary.openTrades || 0)
    };
  }

  async function fetchActiveTrades() {
    const open = await get(CONFIG.ENDPOINTS.tradesActive);
    const rows = open.trades || [];
    return rows.map((t) => {
      const currentPrice = Number(t.entryPrice || 0);
      const pnl = (currentPrice - Number(t.entryPrice || 0)) * Number(t.quantity || 0);
      const pnlPct = Number(t.entryPrice) > 0 ? ((currentPrice - Number(t.entryPrice)) / Number(t.entryPrice)) * 100 : 0;
      return {
        id: t.id,
        symbol: t.symbol,
        entryPrice: Number(t.entryPrice || 0),
        currentPrice,
        quantity: Number(t.quantity || 0),
        stopLoss: Number(t.stopLoss || 0),
        takeProfit: Number(t.takeProfit || 0),
        aiConfidence: Number(t.aiConfidence || 0),
        pnl: Number(pnl || 0),
        pnlPercentage: Number(pnlPct || 0),
        status: t.status,
        entryTime: t.entryTime
      };
    });
  }

  async function fetchTradeHistory() {
    const res = await get(CONFIG.ENDPOINTS.tradesHistory);
    const rows = res.data || [];
    return rows.map((t) => ({
      id: t.id,
      symbol: t.symbol,
      entryPrice: Number(t.entryPrice || 0),
      exitPrice: Number(t.exitPrice || 0),
      quantity: Number(t.quantity || 0),
      pnl: Number(t.pnL || 0),
      pnlPercentage: Number(t.pnLPercentage || 0),
      status: Number(t.pnL || 0) > 0 ? "WIN" : "LOSS",
      entryTime: t.entryTime,
      exitTime: t.exitTime,
      duration: (t.entryTime && t.exitTime) ? fmt.timeAgo(t.entryTime) : "--",
      aiConfidence: Number(t.aiConfidence || 0)
    }));
  }

  async function fetchOrders() {
    const res = await get(CONFIG.ENDPOINTS.orders);
    const trades = res.data || [];
    const orders = [];
    await Promise.allSettled(trades.slice(0, 20).map(async (t) => {
      const detail = await get(CONFIG.ENDPOINTS.tradeById(t.id));
      (detail.orders || []).forEach((o) => {
        orders.push({
          id: o.id,
          symbol: o.symbol,
          externalOrderId: o.externalOrderId,
          side: (o.status === 2 || o.status === 3) ? "SELL" : "BUY",
          quantity: Number(o.quantity || 0),
          executedPrice: Number(o.executedPrice || 0),
          status: typeof o.status === "number" ? mapTradeStatus(o.status) : o.status,
          createdAt: o.createdAt
        });
      });
    }));
    return orders.sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt));
  }

  async function fetchSignals() {
    const res = await get(CONFIG.ENDPOINTS.signals);
    const signals = res.signals || [];
    return signals.map((s) => ({
      id: s.id,
      direction: (s.action === 1 || s.action === "Buy") ? "BUY" : "SELL",
      symbol: s.symbol,
      confidence: Number(s.aiConfidence || 0),
      ema: Number(s.entryPrice || 0),
      rsi: 50,
      macd: 0,
      timestamp: s.createdAt
    }));
  }

  async function fetchAIData() {
    const res = await get(CONFIG.ENDPOINTS.aiVerifications);
    const list = res.data || [];
    return list.map((v) => ({
      model: parseModel(v.rawResponse),
      confidence: Number(v.confidence || 0),
      decision: (v.parsedAction || "HOLD").toUpperCase(),
      fallback: false,
      responseTime: 0,
      symbol: v.symbol,
      timestamp: v.timestamp,
      reasoning: v.rawResponse || ""
    }));
  }

  async function fetchRiskProfile() {
    const profile = await get(CONFIG.ENDPOINTS.riskProfile);
    const holdings = await get(CONFIG.ENDPOINTS.accountHoldings).catch(() => ({ totalUnrealizedPnL: 0, totalOpenPositions: 0 }));
    return {
      riskPerTrade: Number(profile.maxRiskPerTradePercent || 0) * 100,
      maxDailyLoss: Number(profile.maxDailyLossPercent || 0) * 100,
      maxOpenTrades: Number(profile.maxTradesPerDay || 0),
      currentExposure: Number(holdings.totalUnrealizedPnL || 0),
      dailyDrawdown: 0,
      openTradesCount: Number(holdings.totalOpenPositions || 0),
      usedMargin: 0
    };
  }

  async function fetchLogs() {
    const res = await get(`${CONFIG.ENDPOINTS.systemLogs}?page=1&pageSize=30`);
    return (res.data || []).map((l) => ({
      id: l.id,
      time: l.createdAt,
      level: (l.level || "INFO").toUpperCase(),
      message: l.message || ""
    }));
  }

  async function fetchSymbols() {
    const res = await getCached("active_symbols", 5 * 60 * 1000, async () =>
      get(CONFIG.ENDPOINTS.marketPairsActive).catch(() => ({ pairs: [] }))
    );
    const pairs = res.pairs || [];
    return pairs.map((p) => p.symbol).filter(Boolean);
  }

  async function closeTrade(id) {
    return post(CONFIG.ENDPOINTS.tradeClose(id), {});
  }

  async function updateRiskProfile(data) {
    const existing = await get(CONFIG.ENDPOINTS.riskProfile);
    const payload = {
      ...existing,
      maxRiskPerTradePercent: Number(data.riskPerTrade) / 100,
      maxDailyLossPercent: Number(data.maxDailyLoss) / 100,
      maxTradesPerDay: Number(data.maxOpenTrades)
    };
    return put(CONFIG.ENDPOINTS.riskUpdate, payload);
  }

  async function startBot() {
    throw new Error("Backend start endpoint is not available.");
  }

  async function stopBot() {
    throw new Error("Backend stop endpoint is not available.");
  }

  function mapTradeStatus(statusNum) {
    const map = { 1: "PENDING", 2: "OPEN", 3: "FILLED", 4: "CANCELED", 5: "FAILED" };
    return map[statusNum] || String(statusNum);
  }

  function parseModel(rawResponse) {
    const text = String(rawResponse || "");
    const m = text.match(/Provider=([^/]+)\/([^\s|]+)/i);
    if (m) return `${m[1]} ${m[2]}`;
    return "AI Model";
  }

  return {
    get, post, put, del,
    setApiKey, getApiKey,
    getBinanceCandles, getBinanceTicker, getBinanceAllTickers,
    fetchSystemStatus, fetchAccount, fetchActiveTrades, fetchTradeHistory, fetchOrders,
    fetchSignals, fetchAIData, fetchRiskProfile, fetchLogs, fetchSymbols,
    closeTrade, updateRiskProfile, startBot, stopBot
  };
})();

window.ApiClient = ApiClient;
