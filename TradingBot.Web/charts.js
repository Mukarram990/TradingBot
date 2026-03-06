/* ============================================================
   CHARTS.JS — Candlestick, Indicators & Performance Charts
   CryptoBot Dashboard v2
   ============================================================ */

const Charts = (() => {

  let candleChart      = null;
  let volumeChart      = null;
  let pnlLineChart     = null;
  let winRateChart     = null;
  let rsiChart         = null;
  let macdChart        = null;

  let currentCandles   = [];
  let showEMA          = true;
  let showVolume       = true;
  let showRSI          = false;
  let showMACD         = false;

  const CHART_OPTS = {
    layout: {
      background:  { color: "transparent" },
      textColor:   "#7a9cc0",
      fontFamily:  "'JetBrains Mono', monospace",
    },
    grid: {
      vertLines:   { color: "rgba(0,212,255,0.04)" },
      horzLines:   { color: "rgba(0,212,255,0.04)" },
    },
    crosshair: {
      mode: 1,
      vertLine: { color: "rgba(0,212,255,0.5)", labelBackgroundColor: "#162032" },
      horzLine: { color: "rgba(0,212,255,0.5)", labelBackgroundColor: "#162032" },
    },
    timeScale: {
      borderColor:          "rgba(0,212,255,0.08)",
      timeVisible:          true,
      secondsVisible:       false,
      rightOffset:          8,
      barSpacing:           8,
      minBarSpacing:        4,
    },
    rightPriceScale: {
      borderColor: "rgba(0,212,255,0.08)",
      scaleMargins: { top: 0.1, bottom: 0.1 },
    },
    handleScroll:    { vertTouchDrag: false },
    handleScale:     { axisPressedMouseMove: { time: true, price: false } },
  };

  /* ══════════════════════════════════════
     MAIN CANDLE CHART
  ══════════════════════════════════════ */
  function initCandleChart(containerId) {
    const container = document.getElementById(containerId);
    if (!container || typeof LightweightCharts === "undefined") {
      initFallbackChart(containerId);
      return;
    }

    container.innerHTML = "";
    const height = container.clientHeight || 360;
    const width  = container.clientWidth  || 800;

    candleChart = LightweightCharts.createChart(container, {
      ...CHART_OPTS,
      width, height,
    });

    // Candle Series
    window._candleSeries = candleChart.addCandlestickSeries({
      upColor:          "#00e676",
      downColor:        "#ff3b5c",
      borderUpColor:    "#00e676",
      borderDownColor:  "#ff3b5c",
      wickUpColor:      "#00a050",
      wickDownColor:    "#c0002a",
    });

    // EMA Series
    window._ema20Series = candleChart.addLineSeries({
      color:       "rgba(0,212,255,0.8)",
      lineWidth:   1.5,
      lineStyle:   0,
      priceLineVisible: false,
    });

    window._ema50Series = candleChart.addLineSeries({
      color:       "rgba(255,112,67,0.7)",
      lineWidth:   1.5,
      lineStyle:   0,
      priceLineVisible: false,
    });

    // Volume Series
    window._volumeSeries = candleChart.addHistogramSeries({
      color:       "rgba(0,212,255,0.3)",
      priceFormat: { type: "volume" },
      priceScaleId: "volume",
      scaleMargins: { top: 0.8, bottom: 0 },
    });

    candleChart.priceScale("volume").applyOptions({
      scaleMargins: { top: 0.8, bottom: 0 },
    });

    // Crosshair hover
    candleChart.subscribeCrosshairMove(param => {
      if (!param.time) return;
      const bar = param.seriesData.get(window._candleSeries);
      if (bar) updateOHLCDisplay(bar);
    });

    // Handle resize
    const ro = new ResizeObserver(() => {
      if (candleChart) {
        candleChart.applyOptions({
          width:  container.clientWidth,
          height: container.clientHeight,
        });
      }
    });
    ro.observe(container);

    loadCandleData();
    return candleChart;
  }

  async function loadCandleData(symbol, tf) {
    symbol = symbol || AppState.selectedSymbol;
    tf     = tf     || AppState.selectedTf;

    const container = document.getElementById("chart-loading");
    if (container) container.style.display = "flex";

    try {
      const candles = await ApiClient.getBinanceCandles(symbol, tf, CONFIG.CHART_CANDLE_LIMIT);
      currentCandles = candles;
      applyCandles(candles);
    } finally {
      if (container) container.style.display = "none";
    }
  }

  function applyCandles(candles) {
    if (!window._candleSeries) return;

    window._candleSeries.setData(candles);

    // Volume
    if (window._volumeSeries) {
      window._volumeSeries.setData(candles.map(c => ({
        time:  c.time,
        value: c.volume,
        color: c.close >= c.open
          ? "rgba(0,230,118,0.3)"
          : "rgba(255,59,92,0.3)",
      })));
    }

    // EMA 20
    if (window._ema20Series) {
      const ema20 = calcEMA(candles.map(c => c.close), 20);
      window._ema20Series.setData(
        candles.slice(19).map((c, i) => ({ time: c.time, value: ema20[i] }))
      );
    }

    // EMA 50
    if (window._ema50Series) {
      const ema50 = calcEMA(candles.map(c => c.close), 50);
      window._ema50Series.setData(
        candles.slice(49).map((c, i) => ({ time: c.time, value: ema50[i] }))
      );
    }

    updateCurrentPrice(candles[candles.length - 1]);
  }

  function updateOHLCDisplay(bar) {
    const el = document.getElementById("ohlc-display");
    if (!el) return;
    el.innerHTML = `
      <span style="color:var(--text-muted)">O</span>
      <span>${fmt.price(bar.open)}</span>
      <span style="color:var(--text-muted);margin-left:8px">H</span>
      <span style="color:var(--green)">${fmt.price(bar.high)}</span>
      <span style="color:var(--text-muted);margin-left:8px">L</span>
      <span style="color:var(--red)">${fmt.price(bar.low)}</span>
      <span style="color:var(--text-muted);margin-left:8px">C</span>
      <span>${fmt.price(bar.close)}</span>
    `;
  }

  function updateCurrentPrice(lastBar) {
    const priceEl  = document.getElementById("chart-current-price");
    const changeEl = document.getElementById("chart-price-change");
    if (!priceEl || !lastBar) return;

    priceEl.textContent = fmt.price(lastBar.close);

    if (currentCandles.length > 1) {
      const prev = currentCandles[0].close;
      const pct  = ((lastBar.close - prev) / prev * 100).toFixed(2);
      const up   = pct >= 0;
      changeEl.textContent = `${up ? "▲" : "▼"} ${Math.abs(pct)}%`;
      changeEl.className   = `chart-change ${up ? "text-green" : "text-red"}`;
    }
  }

  /* ══════════════════════════════════════
     RSI PANEL
  ══════════════════════════════════════ */
  function initRSIChart(containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const canvas = document.createElement("canvas");
    canvas.height = 80;
    container.innerHTML = "";
    container.appendChild(canvas);

    const ctx = canvas.getContext("2d");
    const closes = currentCandles.map(c => c.close);
    const rsiData = calcRSI(closes, 14);

    rsiChart = new Chart(ctx, {
      type: "line",
      data: {
        labels: currentCandles.slice(14).map(c => ""),
        datasets: [{
          data:         rsiData,
          borderColor:  "rgba(255,112,67,0.8)",
          borderWidth:  1.5,
          pointRadius:  0,
          fill:         false,
          tension:      0.3,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: { legend: { display: false }, tooltip: { mode: "index" }},
        scales: {
          x: { display: false },
          y: {
            min: 0, max: 100,
            grid:  { color: "rgba(0,212,255,0.04)" },
            ticks: { color: "#4a6080", font: { size: 9 } },
          },
        },
      },
    });
  }

  /* ══════════════════════════════════════
     PnL CHART
  ══════════════════════════════════════ */
  function initPnLChart(containerId, trades) {
    const container = document.getElementById(containerId);
    if (!container) return;

    container.innerHTML = "";
    const canvas = document.createElement("canvas");
    container.appendChild(canvas);
    const ctx = canvas.getContext("2d");

    if (pnlLineChart) pnlLineChart.destroy();

    const sorted = [...trades].sort((a, b) => new Date(a.exitTime) - new Date(b.exitTime));
    let cumPnl = 0;
    const labels = [];
    const data   = [];
    const colors = [];

    sorted.forEach((t, i) => {
      cumPnl += t.pnl;
      labels.push(i + 1);
      data.push(parseFloat(cumPnl.toFixed(2)));
      colors.push(t.pnl > 0 ? "rgba(0,230,118,0.8)" : "rgba(255,59,92,0.8)");
    });

    // Gradient fill
    const grad = ctx.createLinearGradient(0, 0, 0, 180);
    grad.addColorStop(0, "rgba(0,212,255,0.2)");
    grad.addColorStop(1, "rgba(0,212,255,0)");

    pnlLineChart = new Chart(ctx, {
      type: "line",
      data: {
        labels,
        datasets: [{
          label:          "Cumulative PnL",
          data,
          borderColor:    "rgba(0,212,255,0.9)",
          borderWidth:    2,
          pointRadius:    2,
          pointBackgroundColor: colors,
          pointBorderColor:    colors,
          fill:           true,
          backgroundColor: grad,
          tension:        0.4,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: "index", intersect: false },
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: "rgba(17,24,39,0.95)",
            borderColor:     "rgba(0,212,255,0.2)",
            borderWidth:     1,
            titleColor:      "#7a9cc0",
            bodyColor:       "#e8f4ff",
            callbacks: {
              label: ctx => `PnL: $${ctx.parsed.y.toFixed(2)}`,
            },
          },
        },
        scales: {
          x: {
            grid:  { color: "rgba(0,212,255,0.03)" },
            ticks: { color: "#4a6080", font: { size: 9 } },
          },
          y: {
            grid:  { color: "rgba(0,212,255,0.05)" },
            ticks: {
              color: "#4a6080",
              font:  { size: 9 },
              callback: v => `$${v.toFixed(0)}`,
            },
          },
        },
      },
    });

    return pnlLineChart;
  }

  /* ══════════════════════════════════════
     WIN RATE DOUGHNUT
  ══════════════════════════════════════ */
  function initWinRateChart(containerId, winRate) {
    const container = document.getElementById(containerId);
    if (!container) return;

    container.innerHTML = "";
    const canvas = document.createElement("canvas");
    container.appendChild(canvas);
    const ctx = canvas.getContext("2d");

    if (winRateChart) winRateChart.destroy();

    winRateChart = new Chart(ctx, {
      type: "doughnut",
      data: {
        datasets: [{
          data:            [winRate, 100 - winRate],
          backgroundColor: [
            "rgba(0,230,118,0.85)",
            "rgba(30,46,74,0.8)",
          ],
          borderColor: ["rgba(0,230,118,0.5)", "transparent"],
          borderWidth: [2, 0],
          hoverOffset: 3,
        }],
      },
      options: {
        responsive: true,
        maintainAspectRatio: true,
        cutout: "78%",
        plugins: {
          legend:  { display: false },
          tooltip: { enabled: false },
        },
      },
    });

    return winRateChart;
  }

  /* ══════════════════════════════════════
     SPARKLINE MINI CHART
  ══════════════════════════════════════ */
  function createSparkline(canvasEl, data, color) {
    if (!canvasEl) return;
    const ctx  = canvasEl.getContext("2d");
    const min  = Math.min(...data);
    const max  = Math.max(...data);
    const rng  = max - min || 1;
    const w    = canvasEl.width  = canvasEl.offsetWidth  || 80;
    const h    = canvasEl.height = canvasEl.offsetHeight || 24;

    ctx.clearRect(0, 0, w, h);
    ctx.beginPath();
    data.forEach((v, i) => {
      const x = (i / (data.length - 1)) * w;
      const y = h - ((v - min) / rng) * h * 0.8 - h * 0.1;
      i === 0 ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    });
    ctx.strokeStyle = color || "rgba(0,212,255,0.8)";
    ctx.lineWidth   = 1.5;
    ctx.stroke();
  }

  /* ══════════════════════════════════════
     FALLBACK CHART (Chart.js)
  ══════════════════════════════════════ */
  function initFallbackChart(containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;

    container.innerHTML = "";
    const canvas = document.createElement("canvas");
    container.appendChild(canvas);
    const ctx = canvas.getContext("2d");

    ApiClient.getBinanceCandles(
      AppState.selectedSymbol,
      AppState.selectedTf,
      80
    ).then(candles => {
      currentCandles = candles;
      const labels = candles.map((c, i) => i % 10 === 0
        ? new Date(c.time * 1000).toLocaleDateString()
        : ""
      );

      new Chart(ctx, {
        type: "line",
        data: {
          labels,
          datasets: [{
            data:            candles.map(c => c.close),
            borderColor:     "rgba(0,212,255,0.8)",
            borderWidth:     1.5,
            pointRadius:     0,
            fill:            true,
            backgroundColor: "rgba(0,212,255,0.06)",
            tension:         0.3,
          }],
        },
        options: {
          responsive: true,
          maintainAspectRatio: false,
          plugins: { legend: { display: false } },
          scales: {
            x: {
              grid:  { color: "rgba(0,212,255,0.04)" },
              ticks: { color: "#4a6080", font: { size: 9 } },
            },
            y: {
              grid:  { color: "rgba(0,212,255,0.05)" },
              ticks: { color: "#4a6080", font: { size: 9 } },
            },
          },
        },
      });

      updateCurrentPrice(candles[candles.length - 1]);
    });
  }

  /* ══════════════════════════════════════
     INDICATOR MATH
  ══════════════════════════════════════ */
  function calcEMA(prices, period) {
    const k      = 2 / (period + 1);
    const result = [];
    let ema      = prices.slice(0, period).reduce((s, v) => s + v, 0) / period;

    for (let i = period; i < prices.length; i++) {
      ema = prices[i] * k + ema * (1 - k);
      result.push(parseFloat(ema.toFixed(4)));
    }
    return result;
  }

  function calcRSI(prices, period = 14) {
    const result = [];
    for (let i = period; i < prices.length; i++) {
      let gains = 0, losses = 0;
      for (let j = i - period + 1; j <= i; j++) {
        const d = prices[j] - prices[j - 1];
        if (d > 0) gains  += d;
        else       losses -= d;
      }
      const rs  = gains / (losses || 0.0001);
      result.push(parseFloat((100 - 100 / (1 + rs)).toFixed(2)));
    }
    return result;
  }

  function calcMACD(prices, fast = 12, slow = 26, signal = 9) {
    const emaFast = calcEMA(prices, fast);
    const emaSlow = calcEMA(prices, slow);
    const offset  = slow - fast;
    const macdLine = emaSlow.map((v, i) => parseFloat((emaFast[i + offset] - v).toFixed(4)));
    const signalLine = calcEMA(macdLine, signal);
    const histogram  = signalLine.map((v, i) =>
      parseFloat((macdLine[i + signal - 1] - v).toFixed(4))
    );
    return { macdLine, signalLine, histogram };
  }

  /* ══════════════════════════════════════
     CHART CONTROLS
  ══════════════════════════════════════ */
  function setSymbol(symbol) {
    AppState.selectedSymbol = symbol;
    loadCandleData(symbol, AppState.selectedTf);
  }

  function setTimeframe(tf) {
    AppState.selectedTf = tf;
    loadCandleData(AppState.selectedSymbol, tf);
    document.querySelectorAll(".tf-btn").forEach(b =>
      b.classList.toggle("active", b.dataset.tf === tf)
    );
  }

  function toggleEMA(active) {
    showEMA = active;
    if (window._ema20Series) window._ema20Series.applyOptions({ visible: showEMA });
    if (window._ema50Series) window._ema50Series.applyOptions({ visible: showEMA });
  }

  function toggleVolume(active) {
    showVolume = active;
    if (window._volumeSeries) window._volumeSeries.applyOptions({ visible: showVolume });
  }

  function destroyAll() {
    if (candleChart)    { candleChart.remove(); candleChart = null; }
    if (pnlLineChart)   { pnlLineChart.destroy();  pnlLineChart = null; }
    if (winRateChart)   { winRateChart.destroy();   winRateChart = null; }
    if (rsiChart)       { rsiChart.destroy();       rsiChart = null; }
    window._candleSeries = null;
    window._ema20Series  = null;
    window._ema50Series  = null;
    window._volumeSeries = null;
  }

  /* ══════════════════════════════════════
     HEATMAP DATA
  ══════════════════════════════════════ */
  async function renderHeatmap(containerId) {
    const container = document.getElementById(containerId);
    if (!container) return;

    const tickers = await ApiClient.getBinanceAllTickers(CONFIG.WATCH_SYMBOLS);
    container.innerHTML = "";

    CONFIG.WATCH_SYMBOLS.forEach(sym => {
      const t   = tickers[sym];
      const pct = t ? parseFloat(t.priceChangePercent) : 0;
      const bg  = pct > 3  ? "#00a050" :
                  pct > 1  ? "#007040" :
                  pct > 0  ? "#004030" :
                  pct > -1 ? "#400020" :
                  pct > -3 ? "#800030" : "#c0002a";

      const cell = document.createElement("div");
      cell.className   = "heatmap-cell";
      cell.style.background = bg;
      cell.dataset.sym = sym;
      cell.innerHTML   = `
        <span class="hm-symbol">${sym.replace("USDT","")}</span>
        <span class="hm-change">${pct >= 0 ? "+" : ""}${pct.toFixed(2)}%</span>
      `;
      cell.addEventListener("click", () => {
        setSymbol(sym);
        navigateTo("chart");
      });
      container.appendChild(cell);
    });
  }

  return {
    initCandleChart,
    initFallbackChart,
    initPnLChart,
    initWinRateChart,
    initRSIChart,
    createSparkline,
    loadCandleData,
    setSymbol,
    setTimeframe,
    toggleEMA,
    toggleVolume,
    renderHeatmap,
    calcEMA,
    calcRSI,
    calcMACD,
    destroyAll,
    getCurrentCandles: () => currentCandles,
  };
})();

window.Charts = Charts;
