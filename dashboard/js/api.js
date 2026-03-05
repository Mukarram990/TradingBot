/* global window */
(function () {
  const store = {
    baseUrl: localStorage.getItem("tb_api_base_url") || "https://localhost:7282",
    apiKey: localStorage.getItem("tb_api_key") || ""
  };

  function setCredentials(baseUrl, apiKey) {
    store.baseUrl = (baseUrl || "").replace(/\/+$/, "");
    store.apiKey = apiKey || "";
    localStorage.setItem("tb_api_base_url", store.baseUrl);
    localStorage.setItem("tb_api_key", store.apiKey);
  }

  async function fetchWithApiKey(endpoint, options = {}) {
    if (!store.apiKey) throw new Error("Missing API key. Set X-API-KEY first.");
    const url = `${store.baseUrl}${endpoint.startsWith("/") ? endpoint : `/${endpoint}`}`;
    const headers = Object.assign({}, options.headers || {}, {
      "X-API-KEY": store.apiKey
    });
    if (!headers["Content-Type"] && options.body) headers["Content-Type"] = "application/json";
    const resp = await fetch(url, Object.assign({}, options, { headers }));
    if (!resp.ok) {
      const txt = await resp.text();
      const err = new Error(`HTTP ${resp.status}: ${txt || resp.statusText}`);
      err.status = resp.status;
      throw err;
    }
    return resp.json();
  }

  const api = {
    getSystemHealth: () => fetchWithApiKey("/api/system/health"),
    getWorkerStatus: () => fetchWithApiKey("/api/system/worker-status"),
    getLogs: () => fetchWithApiKey("/api/system/logs?page=1&pageSize=80"),
    getBalance: () => fetchWithApiKey("/api/portfolio/balance"),
    getHoldings: () => fetchWithApiKey("/api/portfolio/holdings"),
    getTradeSummary: () => fetchWithApiKey("/api/trades/summary"),
    getOpenTrades: () => fetchWithApiKey("/api/trades/open"),
    getTrades: (params) => fetchWithApiKey(`/api/trades?${params}`),
    getTradeById: (id) => fetchWithApiKey(`/api/trades/${id}`),
    getRiskProfile: () => fetchWithApiKey("/api/risk/profile"),
    getSignals: () => fetchWithApiKey("/api/strategy/signals?count=20"),
    getAiStatus: () => fetchWithApiKey("/api/ai/status"),
    getLatestAiResponses: () => fetchWithApiKey("/api/ai/responses/latest"),
    getMarketPrice: (symbol) => fetchWithApiKey(`/api/market/price/${symbol}`),
    getCandles: (symbol, interval = "1h", limit = 100) =>
      fetchWithApiKey(`/api/market/candles?symbol=${encodeURIComponent(symbol)}&interval=${interval}&limit=${limit}`)
  };

  window.TradingBotApi = { setCredentials, fetchWithApiKey, api, store };
})();
