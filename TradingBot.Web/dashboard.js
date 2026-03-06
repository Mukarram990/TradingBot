/* ============================================================
   DASHBOARD.JS — Main Orchestrator
   CryptoBot Dashboard v2
   ============================================================ */

/* ══════════════════════════════════════
   FORMAT UTILITIES
══════════════════════════════════════ */
const fmt = {
  price(v) {
    if (!v && v !== 0) return "--";
    return new Intl.NumberFormat("en-US", {
      minimumFractionDigits: 2,
      maximumFractionDigits: v > 100 ? 2 : v > 1 ? 4 : 6,
    }).format(v);
  },
  qty(v) {
    if (!v && v !== 0) return "--";
    return parseFloat(v).toFixed(4);
  },
  money(v) {
    if (!v && v !== 0) return "--";
    return new Intl.NumberFormat("en-US", {
      style: "currency", currency: "USD",
      minimumFractionDigits: 2,
    }).format(v);
  },
  pct(v, sign = false) {
    const s = sign && v > 0 ? "+" : "";
    return `${s}${parseFloat(v).toFixed(2)}%`;
  },
  time(iso) {
    if (!iso) return "--";
    const d = new Date(iso);
    return d.toLocaleString("en-US", { month:"short", day:"2-digit", hour:"2-digit", minute:"2-digit", hour12:false });
  },
  timeAgo(iso) {
    if (!iso) return "--";
    const diff = Date.now() - new Date(iso).getTime();
    const s = Math.floor(diff / 1000);
    if (s < 60)  return `${s}s ago`;
    if (s < 3600) return `${Math.floor(s/60)}m ago`;
    return `${Math.floor(s/3600)}h ago`;
  },
};
window.fmt = fmt;

/* ══════════════════════════════════════
   UI HELPERS
══════════════════════════════════════ */
const UI = {

  toast(title, msg, type = "info", duration = CONFIG.TOAST_DURATION) {
    const icons = { success:"✅", error:"❌", info:"ℹ️", warn:"⚠️" };
    const container = document.getElementById("toast-container");
    if (!container) return;

    const toast = document.createElement("div");
    toast.className = `toast ${type}`;
    toast.innerHTML = `
      <span class="toast-icon">${icons[type]}</span>
      <div class="toast-body">
        <div class="toast-title">${title}</div>
        ${msg ? `<div class="toast-msg">${msg}</div>` : ""}
      </div>
      <span class="toast-close" onclick="this.parentElement.remove()">×</span>
    `;

    container.appendChild(toast);
    setTimeout(() => {
      toast.classList.add("removing");
      setTimeout(() => toast.remove(), 300);
    }, duration);
  },

  showModal(title, content, buttons = []) {
    const overlay = document.createElement("div");
    overlay.className = "modal-overlay";
    overlay.id = "modal-overlay";

    const btnHTML = buttons.map(b =>
      `<button class="btn ${b.class}" onclick="${
        typeof b.action === "string"
          ? `UI.closeModal()`
          : `(${b.action.toString()})()`
      }">${b.label}</button>`
    ).join("");

    overlay.innerHTML = `
      <div class="modal-box">
        <div class="modal-title">${title}</div>
        <div class="modal-body">${content}</div>
        ${buttons.length ? `<div class="modal-footer">${btnHTML}</div>` : ""}
      </div>
    `;

    overlay.addEventListener("click", e => {
      if (e.target === overlay) UI.closeModal();
    });

    document.body.appendChild(overlay);
  },

  closeModal() {
    const el = document.getElementById("modal-overlay");
    if (el) el.remove();
  },

  updateElement(id, value) {
    const el = document.getElementById(id);
    if (!el) return;
    if (el.textContent !== String(value)) {
      el.textContent = value;
      el.classList.remove("updated");
      void el.offsetWidth;
      el.classList.add("updated");
    }
  },
};
window.UI = UI;

/* ══════════════════════════════════════
   NAVIGATION
══════════════════════════════════════ */
function navigateTo(view) {
  AppState.activeView = view;

  // Sidebar active state
  document.querySelectorAll(".nav-item").forEach(el =>
    el.classList.toggle("active", el.dataset.view === view)
  );

  // Show correct content panel
  document.querySelectorAll(".main-content").forEach(el =>
    el.classList.toggle("active", el.dataset.view === view)
  );

  // Load data for the view
  switch (view) {
    case "overview":   loadOverview();       break;
    case "chart":      loadChartView();      break;
    case "trades":     loadTradesView();     break;
    case "history":    loadHistoryView();    break;
    case "orders":     loadOrdersView();     break;
    case "ai":         loadAIView();         break;
    case "risk":       loadRiskView();       break;
    case "signals":    loadSignalsView();    break;
    case "heatmap":    loadHeatmapView();    break;
    case "settings":   loadSettingsView();   break;
  }
}
window.navigateTo = navigateTo;

/* ══════════════════════════════════════
   VIEW LOADERS
══════════════════════════════════════ */
async function loadOverview() {
  await Promise.allSettled([
    loadSystemStatus(),
    loadAccountMetrics(),
  ]);
  TradesModule.renderActiveTrades();
}

async function loadSystemStatus() {
  let data;
  try {
    data = await ApiClient.fetchSystemStatus();
  } catch (e) {
    UI.toast("System Status", "Failed to load system status", "warn");
    return;
  }
  AppState.systemStatus = data;
  AppState.botRunning = data.botStatus === "Running";

  UI.updateElement("sys-bot-status",    data.botStatus);
  UI.updateElement("sys-exchange",      data.exchangeConnection);
  UI.updateElement("sys-last-strategy", fmt.timeAgo(data.lastStrategyRun));
  UI.updateElement("sys-last-ai",       fmt.timeAgo(data.lastAIVerification));
  UI.updateElement("sys-signals",       data.totalSignalsGenerated);
  UI.updateElement("sys-uptime",        data.uptime);
  UI.updateElement("sys-version",       data.version || "2.0.1");

  // Bot status dot & button
  const dot = document.getElementById("bot-status-dot");
  if (dot) {
    dot.className = `status-dot ${data.botStatus === "Running" ? "online" : "offline"}`;
  }

  const btn = document.getElementById("bot-toggle-btn");
  if (btn) {
    btn.textContent = data.botStatus === "Running" ? "⏹ Stop Bot" : "▶ Start Bot";
    btn.className   = `btn ${data.botStatus === "Running" ? "btn-danger" : "btn-primary"} btn-sm`;
  }

  // Nav status bar
  const navDot = document.getElementById("nav-bot-dot");
  if (navDot) navDot.className = `status-dot ${data.botStatus === "Running" ? "online" : "offline"}`;

  const navStatus = document.getElementById("nav-bot-status");
  if (navStatus) navStatus.textContent = data.botStatus;
}

async function loadAccountMetrics() {
  let data;
  try {
    data = await ApiClient.fetchAccount();
  } catch (e) {
    UI.toast("Account", "Failed to load account data", "warn");
    return;
  }
  AppState.account = data;

  UI.updateElement("acc-total-balance",    fmt.money(data.totalBalance));
  UI.updateElement("acc-avail-balance",    fmt.money(data.availableBalance));
  UI.updateElement("acc-total-pnl",        `${data.totalPnL >= 0 ? "+" : ""}${fmt.money(data.totalPnL)}`);
  UI.updateElement("acc-daily-pnl",        `${data.dailyPnL >= 0 ? "+" : ""}${fmt.money(data.dailyPnL)}`);
  UI.updateElement("acc-win-rate",         `${data.winRate}%`);
  UI.updateElement("acc-total-trades",     data.totalTrades);
  UI.updateElement("acc-active-trades",    data.activeTrades);

  // Color PnL
  const pnlEl = document.getElementById("acc-total-pnl");
  if (pnlEl) pnlEl.style.color = data.totalPnL >= 0 ? "var(--green)" : "var(--red)";

  const dailyEl = document.getElementById("acc-daily-pnl");
  if (dailyEl) dailyEl.style.color = data.dailyPnL >= 0 ? "var(--green)" : "var(--red)";

  // Update navbar balance
  UI.updateElement("nav-balance", fmt.money(data.totalBalance));

  // Win rate chart
  Charts.initWinRateChart("overview-winrate-chart", data.winRate);
}

async function loadChartView() {
  if (!window._candleChartReady) {
    Charts.initCandleChart("candle-chart-container");
    window._candleChartReady = true;
  }

  // Symbol tabs
  loadSymbolTabs();
}

async function loadTradesView() {
  await TradesModule.renderActiveTrades();
}

async function loadHistoryView() {
  await TradesModule.renderTradeHistory();
}

async function loadOrdersView() {
  await OrdersModule.render();
}

async function loadAIView() {
  await AIPanel.render();
}

async function loadRiskView() {
  await RiskPanel.render();
}

async function loadSignalsView() {
  await renderSignals();
}

async function loadHeatmapView() {
  await Charts.renderHeatmap("heatmap-container");
}

async function loadSettingsView() {
  renderSettings();
}

/* ══════════════════════════════════════
   RIGHT PANEL
══════════════════════════════════════ */
async function renderSignals() {
  const container = document.getElementById("signals-feed");
  if (!container) return;

  const signals = await ApiClient.fetchSignals().catch(() => []);
  AppState.signals = signals;

  // Also update right panel
  const rightSignals = document.getElementById("right-signals-feed");

  const html = signals.slice(0, CONFIG.MAX_SIGNAL_ENTRIES).map(s => `
    <div class="signal-item ${s.direction}">
      <span class="signal-direction ${s.direction}">${s.direction}</span>
      <span class="signal-symbol">${s.symbol}</span>
      <div class="signal-meta">
        RSI:${s.rsi.toFixed(0)} | MACD:${s.macd.toFixed(1)}
      </div>
      <span class="signal-confidence">${s.confidence}%</span>
      <span class="signal-time">${fmt.timeAgo(s.timestamp)}</span>
    </div>
  `).join("");

  const empty = `<div class="text-xs text-muted" style="padding:10px;text-align:center;">No strategy signals yet</div>`;
  if (container) container.innerHTML = html || empty;
  if (rightSignals) rightSignals.innerHTML = html || empty;
}

async function renderRightActiveTrades() {
  const container = document.getElementById("right-active-trades");
  if (!container) return;

  const trades = AppState.activeTrades.length ? AppState.activeTrades
    : await ApiClient.fetchActiveTrades();

  if (!trades.length) {
    container.innerHTML = `<div class="text-xs text-muted" style="padding:10px;text-align:center;">No active trades</div>`;
    return;
  }

  container.innerHTML = trades.map(t => {
    const up = t.pnl >= 0;
    return `
      <div style="padding:8px;border-bottom:1px solid var(--border-subtle);">
        <div style="display:flex;justify-content:space-between;margin-bottom:4px;">
          <span style="font-weight:700;font-size:12px;">${t.symbol}</span>
          <span style="font-size:11px;color:${up?"var(--green)":"var(--red)"};font-weight:600;">
            ${up?"+":""}$${Math.abs(t.pnl).toFixed(2)}
          </span>
        </div>
        <div style="display:flex;justify-content:space-between;">
          <span class="text-xs text-muted">@ ${fmt.price(t.entryPrice)}</span>
          <span class="text-xs ${up?"text-green":"text-red"}">${up?"+":""}${t.pnlPercentage.toFixed(2)}%</span>
        </div>
        <div style="margin-top:4px;">
          <div class="progress-bar-track" style="height:3px;">
            <div class="progress-bar-fill ${up?"green":"red"}"
              style="width:${Math.min(Math.abs(t.pnlPercentage)*10,100)}%">
            </div>
          </div>
        </div>
      </div>
    `;
  }).join("");
}

/* ══════════════════════════════════════
   SYMBOL TABS (Navbar)
══════════════════════════════════════ */
async function loadSymbolTabs() {
  const bar = document.querySelector(".nav-symbol-bar");
  const select = document.getElementById("chart-symbol-select");
  if (!bar && !select) return;

  // Dynamic symbol loading from backend
  try {
    const symbols = await ApiClient.fetchSymbols();
    if (symbols.length) CONFIG.WATCH_SYMBOLS = symbols;
  } catch {
    // Keep existing watch list
  }
  if (!CONFIG.WATCH_SYMBOLS.length) CONFIG.WATCH_SYMBOLS = ["BTCUSDT", "ETHUSDT"];

  if (select) {
    select.innerHTML = CONFIG.WATCH_SYMBOLS
      .map((s) => `<option value="${s}" ${s === AppState.selectedSymbol ? "selected" : ""}>${s}</option>`)
      .join("");
  }

  const tickers = await ApiClient.getBinanceAllTickers(CONFIG.WATCH_SYMBOLS);

  if (bar) bar.innerHTML = CONFIG.WATCH_SYMBOLS.map(sym => {
    const t   = tickers[sym];
    const price = t ? parseFloat(t.lastPrice) : 0;
    const pct   = t ? parseFloat(t.priceChangePercent) : 0;
    const up    = pct >= 0;
    return `
      <div class="symbol-tab ${sym === AppState.selectedSymbol ? "active" : ""}"
        data-sym="${sym}"
        onclick="Charts.setSymbol('${sym}');document.querySelectorAll('.symbol-tab').forEach(e=>e.classList.remove('active'));this.classList.add('active');navigateTo('chart')">
        <span class="sym-name">${sym.replace("USDT","")}</span>
        <span class="sym-price">${fmt.price(price)}</span>
        <span class="sym-change ${up?"text-green":"text-red"}">${up?"+":""}${pct.toFixed(2)}%</span>
      </div>
    `;
  }).join("");
}

/* ══════════════════════════════════════
   BOT CONTROLS
══════════════════════════════════════ */
async function toggleBot() {
  const running = AppState.botRunning;
  const action  = running ? "stop" : "start";

  if (running) {
    UI.showModal(
      "Stop Trading Bot",
      `<p style="color:var(--text-secondary);">Are you sure you want to stop the trading bot?<br>All monitoring will pause.</p>`,
      [
        { label: "Cancel", class: "btn-outline", action: "close" },
        { label: "Stop Bot", class: "btn-danger", action: async () => {
          try {
            await ApiClient.stopBot();
            AppState.botRunning = false;
            UI.closeModal();
            UI.toast("Bot Stopped", "Trading bot has been stopped", "warn");
            Logs.add("Bot stopped by user via dashboard", "WARN");
            loadSystemStatus();
          } catch (e) {
            UI.toast("Error", e.message, "error");
          }
        }},
      ]
    );
  } else {
    try {
      await ApiClient.startBot();
      AppState.botRunning = true;
      UI.toast("Bot Started", "Trading bot is now running", "success");
      Logs.add("Bot started by user via dashboard", "SUCCESS");
      loadSystemStatus();
    } catch (e) {
      UI.toast("Error", e.message, "error");
      // Demo mode - just toggle
      AppState.botRunning = true;
      loadSystemStatus();
    }
  }
}
window.toggleBot = toggleBot;

/* ══════════════════════════════════════
   SETTINGS VIEW
══════════════════════════════════════ */
function renderSettings() {
  const el = document.getElementById("settings-container");
  if (!el) return;

  el.innerHTML = `
    <div class="grid-2" style="gap:16px;">
      <div class="card">
        <div class="card-header">
          <div class="card-title"><span class="title-dot"></span>API Configuration</div>
        </div>
        <div class="card-body">
          <div style="display:flex;flex-direction:column;gap:10px;">
            <div class="input-group">
              <label class="input-label">API Base URL</label>
              <input class="input-field" id="set-api-url" value="${CONFIG.API_BASE_URL}">
            </div>
            <div class="input-group">
              <label class="input-label">API Key</label>
              <input class="input-field" id="set-api-key" type="password" value="${CONFIG.API_KEY}"
                placeholder="YOUR_API_KEY_HERE">
            </div>
            <button class="btn btn-primary btn-sm" onclick="saveAPISettings()">Save API Settings</button>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div class="card-title"><span class="title-dot green"></span>Polling Intervals</div>
        </div>
        <div class="card-body">
          <div style="display:flex;flex-direction:column;gap:10px;">
            <div class="input-group">
              <label class="input-label">Fast Poll (trades/PnL) ms</label>
              <input class="input-field" id="set-poll-fast" type="number" value="${CONFIG.POLL_FAST}">
            </div>
            <div class="input-group">
              <label class="input-label">Mid Poll (orders/signals) ms</label>
              <input class="input-field" id="set-poll-mid" type="number" value="${CONFIG.POLL_MID}">
            </div>
            <div class="input-group">
              <label class="input-label">Slow Poll (account/risk) ms</label>
              <input class="input-field" id="set-poll-slow" type="number" value="${CONFIG.POLL_SLOW}">
            </div>
            <button class="btn btn-outline btn-sm" onclick="savePollingSettings()">Save Intervals</button>
          </div>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div class="card-title"><span class="title-dot"></span>Watch Symbols</div>
        </div>
        <div class="card-body">
          <div class="input-group" style="margin-bottom:10px;">
            <label class="input-label">Symbols (comma separated)</label>
            <input class="input-field" id="set-symbols" value="${CONFIG.WATCH_SYMBOLS.join(",")}">
          </div>
          <button class="btn btn-outline btn-sm" onclick="saveSymbols()">Update Symbols</button>
        </div>
      </div>

      <div class="card">
        <div class="card-header">
          <div class="card-title"><span class="title-dot red"></span>Keyboard Shortcuts</div>
        </div>
        <div class="card-body">
          <div class="shortcuts-grid">
            ${[
              ["Alt+O", "Overview"],
              ["Alt+C", "Chart"],
              ["Alt+T", "Active Trades"],
              ["Alt+H", "Trade History"],
              ["Alt+A", "AI Panel"],
              ["Alt+R", "Risk Panel"],
              ["Alt+S", "Signals"],
              ["Alt+L", "Toggle Logs"],
              ["Alt+B", "Toggle Bot"],
              ["Esc",   "Close Modal"],
            ].map(([key, action]) => `
              <div class="shortcut-item">
                <span class="shortcut-action">${action}</span>
                <span class="kbd">${key}</span>
              </div>
            `).join("")}
          </div>
        </div>
      </div>
    </div>
  `;
}

function saveAPISettings() {
  CONFIG.API_BASE_URL = document.getElementById("set-api-url")?.value || CONFIG.API_BASE_URL;
  CONFIG.API_KEY      = document.getElementById("set-api-key")?.value || CONFIG.API_KEY;
  localStorage.setItem("tb_v2_api_base_url", CONFIG.API_BASE_URL);
  if (window.ApiClient?.setApiKey) ApiClient.setApiKey(CONFIG.API_KEY);
  UI.toast("Settings Saved", "API configuration updated", "success");
  Logs.add("API settings updated", "INFO");
}

function savePollingSettings() {
  CONFIG.POLL_FAST = Math.max(2000, parseInt(document.getElementById("set-poll-fast")?.value) || CONFIG.POLL_FAST);
  CONFIG.POLL_MID  = Math.max(4000, parseInt(document.getElementById("set-poll-mid")?.value)  || CONFIG.POLL_MID);
  CONFIG.POLL_SLOW = Math.max(8000, parseInt(document.getElementById("set-poll-slow")?.value) || CONFIG.POLL_SLOW);
  setupPolling();
  UI.toast("Settings Saved", "Polling intervals updated. Restarted.", "success");
}

function saveSymbols() {
  const val = document.getElementById("set-symbols")?.value;
  if (val) {
    CONFIG.WATCH_SYMBOLS = val.split(",").map(s => s.trim().toUpperCase()).filter(Boolean);
    loadSymbolTabs();
    UI.toast("Symbols Updated", `Watching ${CONFIG.WATCH_SYMBOLS.length} symbols`, "success");
  }
}

window.saveAPISettings    = saveAPISettings;
window.savePollingSettings = savePollingSettings;
window.saveSymbols        = saveSymbols;

/* ══════════════════════════════════════
   CLOCK
══════════════════════════════════════ */
function startClock() {
  function tick() {
    const now = new Date();
    const timeEl = document.getElementById("nav-clock");
    if (timeEl) {
      timeEl.textContent = now.toLocaleTimeString("en-US", {
        hour12: false,
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
      }) + " UTC";
    }
  }
  tick();
  setInterval(tick, 1000);
}

/* ══════════════════════════════════════
   POLLING ENGINE
══════════════════════════════════════ */
function setupPolling() {
  // Clear existing
  Object.values(AppState.intervals).forEach(clearInterval);
  AppState.intervals = {};
  const locks = { fast: false, mid: false, slow: false, logs: false };

  // Fast: active trades PnL
  AppState.intervals.fast = setInterval(async () => {
    if (locks.fast) return;
    locks.fast = true;
    try {
    TradesModule.updateActivePnLLive();
    if (AppState.activeView === "trades" || AppState.activeView === "overview") {
      await TradesModule.renderActiveTrades();
    }
    await renderRightActiveTrades();
    } finally {
      locks.fast = false;
    }
  }, CONFIG.POLL_FAST);

  // Mid: orders, signals, system status
  AppState.intervals.mid = setInterval(async () => {
    if (locks.mid) return;
    locks.mid = true;
    try {
    await loadSystemStatus();
    await renderSignals();
    if (AppState.activeView === "orders") await OrdersModule.render();
    if (AppState.activeView === "overview" || AppState.activeView === "chart" || AppState.activeView === "heatmap") {
      await loadSymbolTabs();
    }
    } finally {
      locks.mid = false;
    }
  }, CONFIG.POLL_MID);

  // Slow: account deep refresh, risk
  AppState.intervals.slow = setInterval(async () => {
    if (locks.slow) return;
    locks.slow = true;
    try {
    await loadAccountMetrics();
    if (AppState.activeView === "risk") await RiskPanel.render();
    if (AppState.activeView === "ai")   await AIPanel.render();
    } finally {
      locks.slow = false;
    }
  }, CONFIG.POLL_SLOW);

  // Logs
  AppState.intervals.logs = setInterval(async () => {
    if (locks.logs) return;
    locks.logs = true;
    try {
      await Logs.refresh();
    } finally {
      locks.logs = false;
    }
  }, Math.max(CONFIG.POLL_LOGS, 5000));
}

/* ══════════════════════════════════════
   KEYBOARD SHORTCUTS
══════════════════════════════════════ */
function setupKeyboardShortcuts() {
  document.addEventListener("keydown", e => {
    if (e.key === "Escape") { UI.closeModal(); return; }
    if (!e.altKey) return;

    const map = {
      "o": "overview",
      "c": "chart",
      "t": "trades",
      "h": "history",
      "r": "risk",
      "a": "ai",
      "s": "signals",
      "b": () => toggleBot(),
      "l": () => {
        const panel = document.getElementById("logs-panel");
        if (panel) {
          const h = getComputedStyle(document.getElementById("app")).getPropertyValue("--bottom-panel-h");
          const app = document.getElementById("app");
          if (h === "0px" || h === "0") {
            app.style.setProperty("--bottom-panel-h", "200px");
          } else {
            app.style.setProperty("--bottom-panel-h", "0px");
          }
        }
      },
    };

    const action = map[e.key.toLowerCase()];
    if (action) {
      e.preventDefault();
      if (typeof action === "string") navigateTo(action);
      else action();
    }
  });
}

/* ══════════════════════════════════════
   SIDEBAR TOGGLE
══════════════════════════════════════ */
function toggleSidebar() {
  AppState.sidebarCollapsed = !AppState.sidebarCollapsed;
  document.getElementById("app")
    .classList.toggle("sidebar-collapsed", AppState.sidebarCollapsed);

  const labels = document.querySelectorAll(".nav-label, .sidebar-section-label, .bot-label");
  labels.forEach(el => {
    el.style.display = AppState.sidebarCollapsed ? "none" : "";
  });
}
window.toggleSidebar = toggleSidebar;

/* ══════════════════════════════════════
   RIGHT PANEL TABS
══════════════════════════════════════ */
function switchRightTab(tab) {
  document.querySelectorAll(".right-tab").forEach(t =>
    t.classList.toggle("active", t.dataset.tab === tab)
  );
  document.querySelectorAll(".right-panel-content").forEach(c =>
    c.classList.toggle("active", c.dataset.content === tab)
  );
}
window.switchRightTab = switchRightTab;

/* ══════════════════════════════════════
   INIT
══════════════════════════════════════ */
async function init() {
  if (window.ApiClient?.setApiKey && CONFIG.API_KEY) {
    ApiClient.setApiKey(CONFIG.API_KEY);
  }
  startClock();
  await Logs.init();
  await navigateTo("overview");
  await loadSymbolTabs();
  setupPolling();
  setupKeyboardShortcuts();

  // Initial right panel
  switchRightTab("signals");
  await renderSignals();
  await renderRightActiveTrades();

  // Initial log entry
  Logs.add("CryptoBot Dashboard v2 initialized", "SUCCESS");
  Logs.add(`Connected to ${CONFIG.API_BASE_URL}`, "INFO");
  Logs.add(`Polling: fast=${CONFIG.POLL_FAST}ms mid=${CONFIG.POLL_MID}ms slow=${CONFIG.POLL_SLOW}ms`, "INFO");
  Logs.add(`Watching ${CONFIG.WATCH_SYMBOLS.length} symbols: ${CONFIG.WATCH_SYMBOLS.join(", ")}`, "INFO");

  UI.toast("Dashboard Ready", "CryptoBot v2 connected", "success");
}

document.addEventListener("DOMContentLoaded", init);
