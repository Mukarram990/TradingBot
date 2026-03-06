/* global window, document */
(function () {
  const { api, setCredentials, store } = window.TradingBotApi;
  const { buildSortableTable, fmtNum, fmtDate, fmtDuration, esc } = window.TableKit;
  const { initPriceChart, updatePriceChart } = window.ChartKit;

  const CORE_POLL_MS = 5000;
  const HEAVY_POLL_MS = 15000;
  const CHART_POLL_MS = 15000;
  let corePollHandle = null;
  let heavyPollHandle = null;
  let chartPollHandle = null;
  let coreRunning = false;
  let heavyRunning = false;
  let chartRunning = false;
  let state = { openTrades: [], closedTrades: [] };

  function setText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text;
  }

  function toPnL(v) {
    const n = Number(v || 0);
    return `<span class="${n >= 0 ? "pos" : "neg"}">${fmtNum(n)}</span>`;
  }

  function setErrorBanner(err) {
    if (!err) return;
    if (err.status === 429) {
      setText("last-refresh", "Rate limited (429). Dashboard auto-backs off and keeps partial updates.");
      return;
    }
    setText("last-refresh", `Partial refresh issue: ${err.message}`);
  }

  function setAuthDefaults() {
    document.getElementById("api-base-url").value = store.baseUrl || "";
    document.getElementById("api-key").value = store.apiKey || "";
  }

  async function refreshSystem() {
    const [healthR, workerR, aiRespR, pingR] = await Promise.allSettled([
      api.getSystemHealth(),
      api.getWorkerStatus(),
      api.getLatestAiResponses(),
      api.getMarketPrice("BTCUSDT")
    ]);

    const health = healthR.status === "fulfilled" ? healthR.value : null;
    const worker = workerR.status === "fulfilled" ? workerR.value : null;
    const aiResp = aiRespR.status === "fulfilled" ? aiRespR.value : null;
    const ping = pingR.status === "fulfilled" ? pingR.value : null;

    if (health) {
      setText("sys-status", health.status || "-");
      setText("db-status", health.components?.database?.status || "-");
    }
    if (worker) {
      const signalWorker = worker.workers?.find((w) => w.name === "SignalGenerationWorker");
      const monitorWorker = worker.workers?.find((w) => w.name === "TradeMonitoringWorker");
      setText("signal-worker-status", signalWorker?.status || "unknown");
      setText(
        "monitor-worker-status",
        monitorWorker?.status === "unknown" ? "idle/no monitor logs" : (monitorWorker?.status || "unknown")
      );
      setText("last-strategy-tick", signalWorker?.lastSeen ? fmtDate(signalWorker.lastSeen) : "No ticks yet");
    }

    if (aiResp) {
      setText("last-ai-decision", aiResp.data?.[0]?.timestamp ? fmtDate(aiResp.data[0].timestamp) : "No AI decision yet");
    }

    if (ping) {
      setText("last-refresh", `Last refresh: ${new Date().toLocaleTimeString()} | BTC ${fmtNum(ping.price, 2)}`);
    } else {
      setText("last-refresh", `Last refresh: ${new Date().toLocaleTimeString()}`);
    }

    [healthR, workerR, aiRespR, pingR].forEach((r) => {
      if (r.status === "rejected") setErrorBanner(r.reason);
    });
  }

  async function refreshAccount() {
    const [balanceR, summaryR, holdingsR] = await Promise.allSettled([
      api.getBalance(),
      api.getTradeSummary(),
      api.getHoldings()
    ]);

    const balance = balanceR.status === "fulfilled" ? balanceR.value : null;
    const summary = summaryR.status === "fulfilled" ? summaryR.value : null;
    const holdings = holdingsR.status === "fulfilled" ? holdingsR.value : null;

    if (balance) {
      setText("balance-total", fmtNum(balance.totalUsdtValue, 2));
      setText("balance-daily-pnl", `${fmtNum(balance.todayPnL, 2)} (${fmtNum(balance.todayPnLPercent, 2)}%)`);
    }
    if (summary) {
      setText("balance-open-trades", String(summary.openTrades ?? 0));
      setText("summary-win-rate", `${fmtNum(summary.winRate, 2)}%`);
      setText("summary-closed-trades", String(summary.closedTrades ?? 0));
    }
    if (holdings) setText("risk-exposure", fmtNum(holdings.totalUnrealizedPnL || 0, 4));

    const realized = Number(summary?.netPnL || 0);
    const unrealized = Number(holdings?.totalUnrealizedPnL || 0);
    setText("summary-total-pnl", fmtNum(realized + unrealized, 4));

    [balanceR, summaryR, holdingsR].forEach((r) => {
      if (r.status === "rejected") setErrorBanner(r.reason);
    });
  }

  async function refreshTradesAndOrders() {
    const [openR, closedR] = await Promise.allSettled([
      api.getOpenTrades(),
      api.getTrades("status=Closed&page=1&pageSize=50&sortBy=entryTime&desc=true")
    ]);

    const openWrap = openR.status === "fulfilled" ? openR.value : { trades: [] };
    const closedWrap = closedR.status === "fulfilled" ? closedR.value : { data: [] };
    state.openTrades = openWrap.trades || [];
    state.closedTrades = closedWrap.data || [];

    setText("active-trades-count", String(state.openTrades.length));
    setText("trade-history-count", String(state.closedTrades.length));

    buildSortableTable("active-trades-table", [
      { key: "symbol", label: "Symbol" },
      { key: "entryPrice", label: "Entry", render: (r) => fmtNum(r.entryPrice) },
      { key: "quantity", label: "Qty", render: (r) => fmtNum(r.quantity, 6) },
      { key: "stopLoss", label: "SL", render: (r) => fmtNum(r.stopLoss) },
      { key: "takeProfit", label: "TP", render: (r) => fmtNum(r.takeProfit) },
      { key: "aiConfidence", label: "AI %", render: (r) => `${r.aiConfidence ?? 0}` },
      { key: "pnl", label: "Current PnL", render: (r) => toPnL(r.pnl || 0) },
      { key: "status", label: "Status" },
      { key: "entryTime", label: "Entry Time", render: (r) => fmtDate(r.entryTime) }
    ], state.openTrades);

    buildSortableTable("trade-history-table", [
      { key: "symbol", label: "Symbol" },
      { key: "pnl", label: "PnL", render: (r) => toPnL(r.pnL) },
      { key: "pnlPercentage", label: "PnL %", render: (r) => `${fmtNum(r.pnLPercentage, 2)}%` },
      { key: "entryTime", label: "Entry Time", render: (r) => fmtDate(r.entryTime) },
      { key: "exitTime", label: "Exit Time", render: (r) => fmtDate(r.exitTime) },
      { key: "duration", label: "Duration", render: (r) => fmtDuration(r.entryTime, r.exitTime) }
    ], state.closedTrades);

    const tradeIds = [...state.openTrades, ...state.closedTrades].slice(0, 6).map((t) => t.id);
    const detailRows = await Promise.all(tradeIds.map((id) => api.getTradeById(id).catch(() => null)));
    const orders = [];
    detailRows.filter(Boolean).forEach((t) => (t.orders || []).forEach((o) => orders.push(o)));

    setText("orders-count", String(orders.length));
    buildSortableTable("orders-table", [
      { key: "symbol", label: "Symbol" },
      { key: "quantity", label: "Quantity", render: (r) => fmtNum(r.quantity, 6) },
      { key: "executedPrice", label: "Executed", render: (r) => fmtNum(r.executedPrice) },
      { key: "status", label: "Status" },
      { key: "createdAt", label: "Created", render: (r) => fmtDate(r.createdAt) },
      { key: "externalOrderId", label: "Order ID" }
    ], orders);

    [openR, closedR].forEach((r) => {
      if (r.status === "rejected") setErrorBanner(r.reason);
    });
  }

  async function refreshAiPanel() {
    const [statusR, latestR] = await Promise.allSettled([
      api.getAiStatus(),
      api.getLatestAiResponses()
    ]);
    const status = statusR.status === "fulfilled" ? statusR.value : { providers: {} };
    const latest = latestR.status === "fulfilled" ? latestR.value : { data: [] };
    const panel = document.getElementById("ai-panel");
    if (!panel) return;
    const latestRows = (latest.data || []).slice(0, 6);

    panel.innerHTML = latestRows.map((r) => `
      <div class="list-item">
        <span class="pill">${esc(r.symbol)}</span>
        <span class="pill">${esc(r.parsedAction || "-")}</span>
        <span class="pill">${esc(r.confidence)}%</span>
        <div>${esc(r.rawResponse || "").slice(0, 140)}</div>
      </div>
    `).join("") || `<div class="list-item">No AI responses yet.</div>`;

    const providerDown = Object.entries(status.providers || {})
      .filter(([, v]) => v.available === false)
      .map(([k]) => k);
    if (providerDown.length) {
      panel.insertAdjacentHTML("afterbegin",
        `<div class="list-item warn">Providers cooling down: ${esc(providerDown.join(", "))}</div>`);
    }

    [statusR, latestR].forEach((r) => {
      if (r.status === "rejected") setErrorBanner(r.reason);
    });
  }

  async function refreshRiskPanel() {
    const profile = await api.getRiskProfile().catch((e) => {
      setErrorBanner(e);
      return null;
    });
    if (!profile) return;
    setText("risk-per-trade", `${fmtNum((profile.maxRiskPerTradePercent || 0) * 100, 2)}%`);
    setText("risk-max-daily-loss", `${fmtNum((profile.maxDailyLossPercent || 0) * 100, 2)}%`);
    setText("risk-max-trades", String(profile.maxTradesPerDay ?? "-"));
    setText("risk-breaker", `${profile.circuitBreakerLossCount ?? "-"} losses`);
    setText("risk-enabled", profile.isEnabled ? "Enabled" : "Disabled");
  }

  async function refreshSignals() {
    const signalsWrap = await api.getSignals().catch((e) => {
      setErrorBanner(e);
      return { signals: [] };
    });
    const signals = signalsWrap.signals || [];
    setText("signals-count", String(signals.length));
    buildSortableTable("signals-table", [
      { key: "symbol", label: "Symbol" },
      { key: "action", label: "Action" },
      { key: "aiConfidence", label: "Confidence", render: (r) => `${r.aiConfidence ?? 0}%` },
      { key: "entryPrice", label: "Entry", render: (r) => fmtNum(r.entryPrice) },
      { key: "stopLoss", label: "SL", render: (r) => fmtNum(r.stopLoss) },
      { key: "takeProfit", label: "TP", render: (r) => fmtNum(r.takeProfit) },
      { key: "createdAt", label: "Created", render: (r) => fmtDate(r.createdAt) }
    ], signals);
  }

  async function refreshLogs() {
    const wrap = await api.getLogs().catch((e) => {
      setErrorBanner(e);
      return { data: [] };
    });
    const logs = wrap.data || [];
    const panel = document.getElementById("logs-panel");
    setText("logs-count", String(logs.length));
    if (!panel) return;
    if (!logs.length) {
      panel.innerHTML = `<div class="log-line">No logs yet.</div>`;
      return;
    }
    panel.innerHTML = logs.map((l) => `
      <div class="log-line">
        <span class="${l.level === "ERROR" ? "neg" : l.level === "WARN" ? "warn" : "pos"}">[${esc(l.level)}]</span>
        ${esc(new Date(l.createdAt).toLocaleTimeString())}
        ${esc(l.message || "")}
      </div>`).join("");
  }

  async function refreshChart() {
    const symbol = document.getElementById("chart-symbol").value.trim().toUpperCase() || "BTCUSDT";
    const interval = document.getElementById("chart-interval").value || "1h";
    const candles = await api.getCandles(symbol, interval, 100).catch((e) => {
      setErrorBanner(e);
      return [];
    });
    if (!candles.length) return;
    updatePriceChart(candles, symbol, interval);
  }

  async function refreshCore() {
    if (coreRunning) return;
    coreRunning = true;
    try {
      await Promise.all([
        refreshSystem(),
        refreshAccount(),
        refreshTradesAndOrders(),
        refreshSignals()
      ]);
    } finally {
      coreRunning = false;
    }
  }

  async function refreshHeavy() {
    if (heavyRunning) return;
    heavyRunning = true;
    try {
      await Promise.all([
        refreshAiPanel(),
        refreshRiskPanel(),
        refreshLogs()
      ]);
    } finally {
      heavyRunning = false;
    }
  }

  async function refreshChartSafe() {
    if (chartRunning) return;
    chartRunning = true;
    try {
      await refreshChart();
    } finally {
      chartRunning = false;
    }
  }

  function startPolling() {
    if (corePollHandle) clearInterval(corePollHandle);
    if (heavyPollHandle) clearInterval(heavyPollHandle);
    if (chartPollHandle) clearInterval(chartPollHandle);

    refreshCore();
    refreshHeavy();
    refreshChartSafe();

    corePollHandle = setInterval(refreshCore, CORE_POLL_MS);
    heavyPollHandle = setInterval(refreshHeavy, HEAVY_POLL_MS);
    chartPollHandle = setInterval(refreshChartSafe, CHART_POLL_MS);
  }

  function bindUi() {
    const form = document.getElementById("auth-form");
    const chartBtn = document.getElementById("chart-refresh-btn");

    form.addEventListener("submit", (e) => {
      e.preventDefault();
      setCredentials(
        document.getElementById("api-base-url").value.trim(),
        document.getElementById("api-key").value.trim()
      );
      startPolling();
    });
    chartBtn.addEventListener("click", (e) => {
      e.preventDefault();
      refreshChartSafe();
    });
  }

  function init() {
    setAuthDefaults();
    initPriceChart();
    bindUi();
    if (store.apiKey) startPolling();
  }

  document.addEventListener("DOMContentLoaded", init);
})();
