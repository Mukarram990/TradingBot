/* ============================================================
   TRADES.JS — Active & Historical Trades
   CryptoBot Dashboard v2
   ============================================================ */

const TradesModule = (() => {

  let historyFilter = "ALL";
  let sortKey       = "entryTime";
  let sortDir       = "desc";

  /* ══════════════════════════════════════
     ACTIVE TRADES TABLE
  ══════════════════════════════════════ */
  async function renderActiveTrades() {
    const tbody = document.getElementById("active-trades-body");
    if (!tbody) return;

    const trades = await ApiClient.fetchActiveTrades();
    AppState.activeTrades = trades;

    if (!trades || trades.length === 0) {
      tbody.innerHTML = `
        <tr>
          <td colspan="11">
            <div class="table-empty">
              <div class="empty-icon">📊</div>
              <div>No active trades</div>
            </div>
          </td>
        </tr>
      `;
      updateActiveTradesCount(0);
      return;
    }

    updateActiveTradesCount(trades.length);

    tbody.innerHTML = trades.map(t => {
      const pnlUp  = t.pnl >= 0;
      const confCl = t.aiConfidence >= 75 ? "text-green" :
                     t.aiConfidence >= 55 ? "text-yellow" : "text-red";
      const pnlPctFmt = `${pnlUp ? "+" : ""}${t.pnlPercentage.toFixed(2)}%`;
      const pnlFmt    = `${pnlUp ? "+" : ""}$${Math.abs(t.pnl).toFixed(2)}`;

      return `
        <tr data-id="${t.id}">
          <td>
            <div class="td-symbol">${t.symbol}</div>
            <div class="text-xs text-muted">${fmt.time(t.entryTime)}</div>
          </td>
          <td class="td-price">${fmt.price(t.entryPrice)}</td>
          <td class="td-price current-price" data-id="${t.id}">${fmt.price(t.currentPrice)}</td>
          <td>${fmt.qty(t.quantity)}</td>
          <td class="text-red">${fmt.price(t.stopLoss)}</td>
          <td class="text-green">${fmt.price(t.takeProfit)}</td>
          <td class="${confCl}">${t.aiConfidence.toFixed(1)}%</td>
          <td>
            <div class="pnl-cell">
              <span class="${pnlUp ? "td-pnl-positive" : "td-pnl-negative"} pnl-usd" data-id="${t.id}">
                ${pnlFmt}
              </span>
              <span class="text-xs ${pnlUp ? "text-green" : "text-red"} pnl-pct" data-id="${t.id}">
                ${pnlPctFmt}
              </span>
            </div>
          </td>
          <td><span class="badge badge-accent">${t.status}</span></td>
          <td>
            <div class="trade-actions">
              <button class="btn-close-trade" onclick="TradesModule.confirmClose('${t.id}','${t.symbol}')">
                Close
              </button>
            </div>
          </td>
        </tr>
      `;
    }).join("");
  }

  function updateActivePnLLive() {
    if (!AppState.activeTrades.length) return;

    AppState.activeTrades.forEach(t => {
      // Simulate price movement
      const drift = (Math.random() - 0.48) * t.entryPrice * 0.0008;
      t.currentPrice = parseFloat((t.currentPrice + drift).toFixed(2));
      t.pnl          = parseFloat(((t.currentPrice - t.entryPrice) * t.quantity).toFixed(2));
      t.pnlPercentage = parseFloat(((t.currentPrice - t.entryPrice) / t.entryPrice * 100).toFixed(2));

      const up = t.pnl >= 0;

      // Update price cell
      const priceEl = document.querySelector(`.current-price[data-id="${t.id}"]`);
      if (priceEl) {
        priceEl.textContent = fmt.price(t.currentPrice);
        priceEl.style.animation = "none";
        requestAnimationFrame(() => priceEl.style.animation = "dataFlash 0.4s ease");
      }

      // Update PnL cells
      const pnlUsdEl = document.querySelector(`.pnl-usd[data-id="${t.id}"]`);
      if (pnlUsdEl) {
        pnlUsdEl.textContent = `${up ? "+" : ""}$${Math.abs(t.pnl).toFixed(2)}`;
        pnlUsdEl.className   = `${up ? "td-pnl-positive" : "td-pnl-negative"} pnl-usd`;
        pnlUsdEl.setAttribute("data-id", t.id);
      }

      const pnlPctEl = document.querySelector(`.pnl-pct[data-id="${t.id}"]`);
      if (pnlPctEl) {
        pnlPctEl.textContent = `${up ? "+" : ""}${t.pnlPercentage.toFixed(2)}%`;
        pnlPctEl.className   = `text-xs ${up ? "text-green" : "text-red"} pnl-pct`;
        pnlPctEl.setAttribute("data-id", t.id);
      }
    });
  }

  function updateActiveTradesCount(count) {
    const el = document.getElementById("active-trades-count");
    if (el) el.textContent = count;
    const badgeEl = document.querySelector('.nav-item[data-view="trades"] .nav-badge');
    if (badgeEl) badgeEl.textContent = count;
  }

  /* ══════════════════════════════════════
     TRADE HISTORY TABLE
  ══════════════════════════════════════ */
  async function renderTradeHistory() {
    const tbody = document.getElementById("history-trades-body");
    if (!tbody) return;

    const trades = await ApiClient.fetchTradeHistory();
    AppState.tradeHistory = trades;

    const filtered = filterHistory(trades);
    const sorted   = sortTrades(filtered);

    if (!sorted.length) {
      tbody.innerHTML = `
        <tr><td colspan="9">
          <div class="table-empty">
            <div class="empty-icon">📉</div>
            <div>No trades match filters</div>
          </div>
        </td></tr>
      `;
      return;
    }

    tbody.innerHTML = sorted.map(t => {
      const up = t.pnl >= 0;
      return `
        <tr data-id="${t.id}">
          <td>
            <div class="td-symbol">${t.symbol}</div>
          </td>
          <td class="td-price">${fmt.price(t.entryPrice)}</td>
          <td class="td-price">${fmt.price(t.exitPrice)}</td>
          <td>${fmt.qty(t.quantity)}</td>
          <td class="${up ? "td-pnl-positive" : "td-pnl-negative"}">
            ${up ? "+" : ""}$${Math.abs(t.pnl).toFixed(2)}
          </td>
          <td class="${up ? "td-pnl-positive" : "td-pnl-negative"}">
            ${up ? "+" : ""}${t.pnlPercentage.toFixed(2)}%
          </td>
          <td>${fmt.time(t.entryTime)}</td>
          <td>${fmt.time(t.exitTime)}</td>
          <td>${t.duration}</td>
          <td>
            <span class="badge ${up ? "badge-green" : "badge-red"}">
              ${t.status}
            </span>
          </td>
        </tr>
      `;
    }).join("");

    renderHistorySummary(trades);
  }

  function renderHistorySummary(trades) {
    const wins   = trades.filter(t => t.pnl > 0);
    const losses = trades.filter(t => t.pnl <= 0);
    const totalPnL = trades.reduce((s, t) => s + t.pnl, 0);

    const summaryEl = document.getElementById("history-summary");
    if (!summaryEl) return;

    summaryEl.innerHTML = `
      <div class="metric-card green">
        <div class="metric-label">Win Rate</div>
        <div class="metric-value text-green">${(wins.length / trades.length * 100).toFixed(1)}%</div>
        <div class="metric-sub"><span class="text-green">${wins.length} wins</span> / ${trades.length} total</div>
      </div>
      <div class="metric-card ${totalPnL >= 0 ? "green" : "red"}">
        <div class="metric-label">Total PnL</div>
        <div class="metric-value ${totalPnL >= 0 ? "text-green" : "text-red"}">
          ${totalPnL >= 0 ? "+" : ""}$${Math.abs(totalPnL).toFixed(2)}
        </div>
        <div class="metric-sub">From ${trades.length} trades</div>
      </div>
      <div class="metric-card">
        <div class="metric-label">Avg Win</div>
        <div class="metric-value text-green">
          +$${wins.length ? (wins.reduce((s,t)=>s+t.pnl,0)/wins.length).toFixed(2) : "0.00"}
        </div>
      </div>
      <div class="metric-card red">
        <div class="metric-label">Avg Loss</div>
        <div class="metric-value text-red">
          -$${losses.length ? Math.abs(losses.reduce((s,t)=>s+t.pnl,0)/losses.length).toFixed(2) : "0.00"}
        </div>
      </div>
    `;

    // Render PnL chart
    Charts.initPnLChart("pnl-chart-container", trades);
    Charts.initWinRateChart("winrate-chart", wins.length / trades.length * 100);
  }

  function filterHistory(trades) {
    if (historyFilter === "ALL")  return trades;
    if (historyFilter === "WIN")  return trades.filter(t => t.pnl > 0);
    if (historyFilter === "LOSS") return trades.filter(t => t.pnl <= 0);
    return trades.filter(t => t.symbol.includes(historyFilter));
  }

  function setHistoryFilter(filter) {
    historyFilter = filter;
    document.querySelectorAll(".filter-chip").forEach(c =>
      c.classList.toggle("active", c.dataset.filter === filter)
    );
    renderTradeHistory();
  }

  function sortTrades(trades) {
    return [...trades].sort((a, b) => {
      let va = a[sortKey], vb = b[sortKey];
      if (typeof va === "string") va = va.toLowerCase(), vb = vb.toLowerCase();
      const cmp = va < vb ? -1 : va > vb ? 1 : 0;
      return sortDir === "asc" ? cmp : -cmp;
    });
  }

  function setSortKey(key) {
    if (sortKey === key) {
      sortDir = sortDir === "asc" ? "desc" : "asc";
    } else {
      sortKey = key;
      sortDir = "desc";
    }
    document.querySelectorAll(".data-table th[data-sort]").forEach(th => {
      th.classList.remove("sort-asc","sort-desc");
      if (th.dataset.sort === sortKey) th.classList.add(`sort-${sortDir}`);
    });
    renderTradeHistory();
  }

  /* ══════════════════════════════════════
     CLOSE TRADE MODAL
  ══════════════════════════════════════ */
  function confirmClose(id, symbol) {
    UI.showModal(
      `Close Trade — ${symbol}`,
      `<p style="color:var(--text-secondary);font-size:13px;">
        Are you sure you want to manually close trade <strong style="color:var(--text-primary)">${id}</strong>
        on <strong style="color:var(--accent)">${symbol}</strong>?<br><br>
        This will send a market sell order to the exchange.
      </p>`,
      [
        { label: "Cancel", class: "btn-outline", action: "close" },
        { label: "Confirm Close", class: "btn-danger", action: () => executeTradeClosure(id) },
      ]
    );
  }

  async function executeTradeClosure(id) {
    try {
      await ApiClient.closeTrade(id);
      UI.toast("Trade Closed", `Trade ${id} manually closed`, "success");
      Logs.add(`Trade ${id} manually closed via dashboard`, "TRADE");
      UI.closeModal();
      setTimeout(renderActiveTrades, 500);
    } catch (err) {
      UI.toast("Close Failed", err.message, "error");
    }
  }

  /* ══════════════════════════════════════
     EXPORT
  ══════════════════════════════════════ */
  function exportCSV() {
    const trades  = AppState.tradeHistory;
    const headers = ["ID","Symbol","Entry Price","Exit Price","Quantity","PnL","PnL%","Entry Time","Exit Time","Duration","Status"];
    const rows    = trades.map(t =>
      [t.id, t.symbol, t.entryPrice, t.exitPrice, t.quantity,
       t.pnl, t.pnlPercentage, t.entryTime, t.exitTime, t.duration, t.status].join(",")
    );
    const csv  = [headers.join(","), ...rows].join("\n");
    const blob = new Blob([csv], { type: "text/csv" });
    const url  = URL.createObjectURL(blob);
    const a    = document.createElement("a");
    a.href     = url;
    a.download = `trade-history-${new Date().toISOString().slice(0,10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    UI.toast("Export", "Trade history exported", "success");
  }

  return {
    renderActiveTrades,
    updateActivePnLLive,
    renderTradeHistory,
    setHistoryFilter,
    setSortKey,
    confirmClose,
    exportCSV,
  };
})();

window.TradesModule = TradesModule;
