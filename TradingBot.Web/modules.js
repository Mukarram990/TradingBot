/* ============================================================
   ORDERS.JS — Orders Panel
   CryptoBot Dashboard v2
   ============================================================ */

const OrdersModule = (() => {

  async function render() {
    const tbody = document.getElementById("orders-body");
    if (!tbody) return;

    const orders = await ApiClient.fetchOrders();
    AppState.orders = orders;

    if (!orders || !orders.length) {
      tbody.innerHTML = `<tr><td colspan="7"><div class="table-empty"><div class="empty-icon">📋</div><div>No orders found</div></div></td></tr>`;
      return;
    }

    tbody.innerHTML = orders.map(o => {
      const statusCl = o.status === "FILLED"             ? "badge-green"  :
                       o.status === "PARTIALLY_FILLED"    ? "badge-yellow" :
                       o.status === "CANCELED"            ? "badge-red"    : "badge-accent";
      const sideCl   = o.side === "BUY" ? "text-green" : "text-red";

      return `
        <tr>
          <td><span class="td-symbol">${o.symbol}</span></td>
          <td><span class="${sideCl}" style="font-weight:700;">${o.side}</span></td>
          <td>
            <div style="font-size:10px;color:var(--text-muted);">${o.externalOrderId}</div>
          </td>
          <td>${fmt.qty(o.quantity)}</td>
          <td class="td-price">${fmt.price(o.executedPrice)}</td>
          <td><span class="badge ${statusCl}">${o.status}</span></td>
          <td>${fmt.time(o.createdAt)}</td>
        </tr>
      `;
    }).join("");
  }

  return { render };
})();

window.OrdersModule = OrdersModule;


/* ============================================================
   AIPANEL.JS — AI Verification Panel
   CryptoBot Dashboard v2
   ============================================================ */

const AIPanel = (() => {

  async function render() {
    const container = document.getElementById("ai-verifications-container");
    if (!container) return;

    const data = await ApiClient.fetchAIData();
    AppState.aiData = data;

    if (!data || !data.length) {
      container.innerHTML = `<div class="table-empty"><div class="empty-icon">🤖</div><div>No AI data available</div></div>`;
      return;
    }

    container.innerHTML = data.map(v => buildAICard(v)).join("");
    renderAISummary(data);
  }

  function buildAICard(v) {
    const scoreClass = v.confidence >= 75 ? "high" : v.confidence >= 55 ? "medium" : "low";
    const decisionCl = v.decision === "BUY" ? "badge-green" :
                       v.decision === "SELL" ? "badge-red" :
                       v.decision === "HOLD" ? "badge-yellow" : "badge-accent";
    const modelColors = {
      "Gemini Pro":     "#4285f4",
      "Grok-2":         "#1da1f2",
      "Claude-3-Haiku": "#ff7043",
      "GPT-4o":         "#00c97a",
    };
    const color = modelColors[v.model] || "var(--accent)";

    const circumference = 2 * Math.PI * 26;
    const offset = circumference - (v.confidence / 100) * circumference;

    return `
      <div class="ai-verification-card">
        <div class="ai-card-header">
          <div>
            <div class="ai-model-name">${v.model}</div>
            <div class="text-xs text-muted" style="margin-top:2px;">${fmt.timeAgo(v.timestamp)}</div>
          </div>
          <div style="display:flex;gap:6px;align-items:center;">
            ${v.fallback ? `<span class="badge badge-yellow">Fallback</span>` : ""}
            <span class="badge ${decisionCl}">${v.decision}</span>
          </div>
        </div>

        <div class="confidence-display">
          <div class="confidence-ring">
            <svg width="64" height="64" viewBox="0 0 64 64">
              <circle class="track" cx="32" cy="32" r="26" stroke-width="5" fill="none" stroke="var(--bg-elevated)" />
              <circle class="fill" cx="32" cy="32" r="26"
                stroke-width="5" fill="none"
                stroke="${color}"
                stroke-dasharray="${circumference}"
                stroke-dashoffset="${offset}"
                stroke-linecap="round"
                transform="rotate(-90 32 32)"
                style="filter:drop-shadow(0 0 4px ${color}40)"
              />
              <text x="32" y="36" text-anchor="middle" fill="${color}"
                font-family="JetBrains Mono" font-size="11" font-weight="700">
                ${v.confidence.toFixed(0)}%
              </text>
            </svg>
          </div>
          <div class="confidence-details">
            <div class="confidence-score ${scoreClass}">${v.confidence.toFixed(1)}%</div>
            <div class="text-xs text-muted">Confidence Score</div>
            <div style="margin-top:4px;font-size:11px;color:var(--text-secondary);">${v.reasoning}</div>
          </div>
        </div>

        <div style="display:grid;grid-template-columns:repeat(3,1fr);gap:6px;margin-top:10px;">
          <div style="text-align:center;padding:6px;background:var(--bg-elevated);border-radius:4px;">
            <div class="text-xs text-muted">Symbol</div>
            <div style="font-size:11px;font-weight:700;color:var(--text-primary)">${v.symbol}</div>
          </div>
          <div style="text-align:center;padding:6px;background:var(--bg-elevated);border-radius:4px;">
            <div class="text-xs text-muted">Response</div>
            <div style="font-size:11px;font-weight:700;color:var(--accent)">${v.responseTime}ms</div>
          </div>
          <div style="text-align:center;padding:6px;background:var(--bg-elevated);border-radius:4px;">
            <div class="text-xs text-muted">Decision</div>
            <div class="badge ${decisionCl}" style="justify-content:center;">${v.decision}</div>
          </div>
        </div>
      </div>
    `;
  }

  function renderAISummary(data) {
    const el = document.getElementById("ai-summary-bar");
    if (!el) return;

    const avgConf  = data.reduce((s, d) => s + d.confidence, 0) / data.length;
    const avgResp  = data.reduce((s, d) => s + d.responseTime, 0) / data.length;
    const buys     = data.filter(d => d.decision === "BUY").length;
    const fallbacks = data.filter(d => d.fallback).length;

    el.innerHTML = `
      <div style="display:flex;gap:10px;flex-wrap:wrap;">
        <div style="padding:6px 12px;background:var(--bg-elevated);border-radius:4px;border:1px solid var(--border-subtle)">
          <span class="text-xs text-muted">Avg Confidence</span>
          <span style="margin-left:8px;font-weight:700;color:var(--accent)">${avgConf.toFixed(1)}%</span>
        </div>
        <div style="padding:6px 12px;background:var(--bg-elevated);border-radius:4px;border:1px solid var(--border-subtle)">
          <span class="text-xs text-muted">Avg Response</span>
          <span style="margin-left:8px;font-weight:700;color:var(--text-primary)">${avgResp.toFixed(0)}ms</span>
        </div>
        <div style="padding:6px 12px;background:var(--bg-elevated);border-radius:4px;border:1px solid var(--border-subtle)">
          <span class="text-xs text-muted">BUY Signals</span>
          <span style="margin-left:8px;font-weight:700;color:var(--green)">${buys}/${data.length}</span>
        </div>
        <div style="padding:6px 12px;background:var(--bg-elevated);border-radius:4px;border:1px solid var(--border-subtle)">
          <span class="text-xs text-muted">Fallbacks</span>
          <span style="margin-left:8px;font-weight:700;color:${fallbacks>0?"var(--yellow)":"var(--green)"}">${fallbacks}</span>
        </div>
      </div>
    `;
  }

  return { render };
})();

window.AIPanel = AIPanel;


/* ============================================================
   RISKPANEL.JS — Risk Management Panel
   CryptoBot Dashboard v2
   ============================================================ */

const RiskPanel = (() => {

  async function render() {
    const data = await ApiClient.fetchRiskProfile();
    AppState.riskProfile = data;
    renderRiskDisplay(data);
    renderRiskForm(data);
  }

  function renderRiskDisplay(d) {
    const el = document.getElementById("risk-display");
    if (!el) return;

    const expPct  = Math.min((d.currentExposure / d.maxDailyLoss) * 100, 100);
    const drawPct = Math.min((d.dailyDrawdown  / d.maxDailyLoss) * 100, 100);
    const tradePct = Math.min((d.openTradesCount / d.maxOpenTrades) * 100, 100);

    const expColor  = expPct  > 70 ? "red" : expPct  > 40 ? "yellow" : "green";
    const drawColor = drawPct > 70 ? "red" : drawPct > 40 ? "yellow" : "green";

    el.innerHTML = `
      <div class="grid-3" style="margin-bottom:14px;">
        ${riskGauge("Risk/Trade",   d.riskPerTrade  + "%",  (d.riskPerTrade / 5) * 100,  "accent")}
        ${riskGauge("Daily Loss Limit", d.maxDailyLoss + "%", (d.dailyDrawdown / d.maxDailyLoss) * 100, drawColor)}
        ${riskGauge("Open Trades",  d.openTradesCount + "/" + d.maxOpenTrades, tradePct, tradePct > 80 ? "red" : "green")}
      </div>

      <div style="display:flex;flex-direction:column;gap:10px;">
        ${progressRow("Current Exposure", d.currentExposure.toFixed(1) + "%", expPct,  expColor)}
        ${progressRow("Daily Drawdown",   d.dailyDrawdown.toFixed(2) + "%",  drawPct, drawColor)}
        ${progressRow("Used Margin",      d.usedMargin?.toFixed(1) + "%",    d.usedMargin || 0, "accent")}
      </div>

      <div class="grid-2" style="margin-top:14px;gap:8px;">
        <div style="padding:12px;background:var(--bg-elevated);border:1px solid var(--border-subtle);border-radius:var(--radius-sm);">
          <div class="text-xs text-muted">Max Daily Loss</div>
          <div style="font-size:18px;font-weight:700;color:var(--red);margin-top:4px;">${d.maxDailyLoss}%</div>
        </div>
        <div style="padding:12px;background:var(--bg-elevated);border:1px solid var(--border-subtle);border-radius:var(--radius-sm);">
          <div class="text-xs text-muted">Risk Per Trade</div>
          <div style="font-size:18px;font-weight:700;color:var(--accent);margin-top:4px;">${d.riskPerTrade}%</div>
        </div>
      </div>
    `;
  }

  function riskGauge(label, value, pct, color) {
    const circumference = 2 * Math.PI * 28;
    const offset = circumference - Math.min(pct, 100) / 100 * circumference;
    const strokeColor = color === "red"    ? "var(--red)"    :
                        color === "yellow" ? "var(--yellow)" :
                        color === "green"  ? "var(--green)"  : "var(--accent)";

    return `
      <div class="risk-gauge-item">
        <svg width="72" height="72" viewBox="0 0 72 72">
          <circle cx="36" cy="36" r="28" fill="none" stroke="var(--bg-elevated)" stroke-width="6"/>
          <circle cx="36" cy="36" r="28" fill="none"
            stroke="${strokeColor}"
            stroke-width="6"
            stroke-dasharray="${circumference}"
            stroke-dashoffset="${offset}"
            stroke-linecap="round"
            transform="rotate(-90 36 36)"
            style="transition:stroke-dashoffset 0.8s ease;filter:drop-shadow(0 0 4px ${strokeColor})"
          />
          <text x="36" y="38" text-anchor="middle"
            fill="${strokeColor}" font-family="JetBrains Mono" font-size="10" font-weight="700">
            ${value}
          </text>
        </svg>
        <div class="risk-gauge-label">${label}</div>
      </div>
    `;
  }

  function progressRow(label, value, pct, color) {
    return `
      <div class="progress-bar-wrap">
        <div class="progress-bar-label">
          <span>${label}</span>
          <span style="font-weight:700;">${value}</span>
        </div>
        <div class="progress-bar-track">
          <div class="progress-bar-fill ${color}" style="width:${Math.min(pct,100)}%"></div>
        </div>
      </div>
    `;
  }

  function renderRiskForm(d) {
    const el = document.getElementById("risk-form");
    if (!el) return;

    el.innerHTML = `
      <div class="grid-2" style="gap:10px;margin-bottom:12px;">
        <div class="input-group">
          <label class="input-label">Risk Per Trade (%)</label>
          <input class="input-field" type="number" id="inp-risk-per-trade"
            min="0.1" max="10" step="0.1" value="${d.riskPerTrade}">
        </div>
        <div class="input-group">
          <label class="input-label">Max Daily Loss (%)</label>
          <input class="input-field" type="number" id="inp-max-daily-loss"
            min="0.5" max="20" step="0.5" value="${d.maxDailyLoss}">
        </div>
        <div class="input-group">
          <label class="input-label">Max Open Trades</label>
          <input class="input-field" type="number" id="inp-max-trades"
            min="1" max="20" step="1" value="${d.maxOpenTrades}">
        </div>
      </div>
      <button class="btn btn-primary btn-sm" onclick="RiskPanel.save()">
        💾 Save Risk Profile
      </button>
    `;
  }

  async function save() {
    const riskPerTrade   = parseFloat(document.getElementById("inp-risk-per-trade")?.value);
    const maxDailyLoss   = parseFloat(document.getElementById("inp-max-daily-loss")?.value);
    const maxOpenTrades  = parseInt(document.getElementById("inp-max-trades")?.value);

    if (isNaN(riskPerTrade) || isNaN(maxDailyLoss) || isNaN(maxOpenTrades)) {
      UI.toast("Validation", "Please fill all risk fields", "warn");
      return;
    }

    try {
      await ApiClient.updateRiskProfile({ riskPerTrade, maxDailyLoss, maxOpenTrades });
      UI.toast("Risk Profile", "Risk settings updated", "success");
      Logs.add("Risk profile updated via dashboard", "INFO");
      render();
    } catch (err) {
      UI.toast("Save Failed", err.message, "error");
    }
  }

  return { render, save };
})();

window.RiskPanel = RiskPanel;


/* ============================================================
   LOGS.JS — Bot Logs Console
   CryptoBot Dashboard v2
   ============================================================ */

const Logs = (() => {

  let entries    = [];
  let autoScroll = true;
  let paused     = false;
  let filterLevel = "ALL";
  let lastServerLogId = null;

  async function init() {
    try {
      const initial = await ApiClient.fetchLogs();
      if (initial && initial.length) {
        // Load initial logs in reverse (oldest first)
        [...initial].reverse().forEach(l => {
          entries.push(l);
        });
        lastServerLogId = initial[0]?.id ?? null;
      }
    } catch {
      // keep dashboard usable even if log endpoint fails at startup
    }
    renderAll();
  }

  async function refresh() {
    if (paused) return;
    try {
      const newLogs = await ApiClient.fetchLogs();
      if (!newLogs || !newLogs.length) return;

      const latestId = newLogs[0]?.id ?? null;
      if (latestId == null) return;
      if (lastServerLogId == null) {
        lastServerLogId = latestId;
        return;
      }

      const fresh = newLogs
        .filter(l => typeof l.id === "number" && l.id > lastServerLogId)
        .sort((a, b) => a.id - b.id);

      if (!fresh.length) return;

      lastServerLogId = Math.max(lastServerLogId, ...fresh.map(l => l.id));
      fresh.forEach(l => addServerEntry(l));
    } catch { /* silent */ }
  }

  function add(message, level = "INFO") {
    if (paused) return;
    insertEntry({
      time: new Date().toISOString(),
      level: level.toUpperCase(),
      message,
    });
  }

  function addServerEntry(log) {
    if (paused) return;
    insertEntry({
      id: log.id,
      time: log.time || new Date().toISOString(),
      level: (log.level || "INFO").toUpperCase(),
      message: log.message || ""
    });
  }

  function insertEntry(entry) {
    entries.unshift(entry);
    if (entries.length > CONFIG.MAX_LOG_ENTRIES) {
      entries = entries.slice(0, CONFIG.MAX_LOG_ENTRIES);
    }
    renderEntry(entry);
  }

  function renderEntry(entry) {
    const body = document.getElementById("logs-body");
    if (!body) return;

    if (filterLevel !== "ALL" && entry.level !== filterLevel) return;

    const el = document.createElement("div");
    el.className = "log-entry";
    el.innerHTML = `
      <span class="log-time">${new Date(entry.time).toLocaleTimeString("en-US",{hour12:false})}</span>
      <span class="log-level ${entry.level}">${entry.level}</span>
      <span class="log-msg">${highlightLogMessage(entry.message)}</span>
    `;

    // Insert at top
    body.insertBefore(el, body.firstChild);

    // Trim DOM nodes
    while (body.children.length > CONFIG.MAX_LOG_ENTRIES) {
      body.removeChild(body.lastChild);
    }
  }

  function renderAll() {
    const body = document.getElementById("logs-body");
    if (!body) return;

    const filtered = filterLevel === "ALL"
      ? entries
      : entries.filter(e => e.level === filterLevel);

    if (!filtered.length) {
      body.innerHTML = `<div class="log-entry"><span class="log-msg text-muted">No logs yet</span></div>`;
      return;
    }

    body.innerHTML = filtered.slice(0, 80).map(e => `
      <div class="log-entry">
        <span class="log-time">${new Date(e.time).toLocaleTimeString("en-US",{hour12:false})}</span>
        <span class="log-level ${e.level}">${e.level}</span>
        <span class="log-msg">${highlightLogMessage(e.message)}</span>
      </div>
    `).join("");
  }

  function highlightLogMessage(msg) {
    // Highlight symbols
    msg = msg.replace(/\b(BTC|ETH|SOL|BNB|XRP|AVAX)USDT\b/g,
      '<span class="highlight">$&</span>');
    // Highlight numbers
    msg = msg.replace(/(\+\$[\d,.]+|-\$[\d,.]+)/g,
      v => `<span style="color:${v.startsWith("+") ? "var(--green)" : "var(--red)"};font-weight:600">${v}</span>`
    );
    // Highlight percentages
    msg = msg.replace(/([\d.]+%)/g,
      '<span style="color:var(--accent)">$1</span>');
    return msg;
  }

  function setFilter(level) {
    filterLevel = level;
    document.querySelectorAll(".log-filter-btn").forEach(b =>
      b.classList.toggle("active", b.dataset.level === level)
    );
    renderAll();
  }

  function setPaused(p) {
    paused = p;
    const btn = document.getElementById("logs-pause-btn");
    if (btn) btn.textContent = p ? "▶ Resume" : "⏸ Pause";
  }

  function clear() {
    entries = [];
    const body = document.getElementById("logs-body");
    if (body) body.innerHTML = "";
  }

  return { init, refresh, add, setFilter, setPaused, clear };
})();

window.Logs = Logs;
