/* global window, document, Chart */
(function () {
  let priceChart;

  function initPriceChart() {
    const ctx = document.getElementById("price-chart");
    if (!ctx) return;

    priceChart = new Chart(ctx, {
      type: "line",
      data: {
        labels: [],
        datasets: [{
          label: "Price",
          data: [],
          borderColor: "#3ea8ff",
          borderWidth: 2,
          pointRadius: 0,
          tension: 0.28,
          fill: true,
          backgroundColor: "rgba(62,168,255,0.1)"
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        scales: {
          x: { ticks: { color: "#88a0bd" }, grid: { color: "#182338" } },
          y: { ticks: { color: "#88a0bd" }, grid: { color: "#182338" } }
        },
        plugins: {
          legend: { labels: { color: "#d7e1f2" } }
        }
      }
    });
  }

  function updatePriceChart(candles, symbol, interval) {
    if (!priceChart) return;
    const labels = candles.map((c) => new Date(c.openTime).toLocaleTimeString());
    const values = candles.map((c) => Number(c.close));
    priceChart.data.labels = labels;
    priceChart.data.datasets[0].data = values;
    priceChart.data.datasets[0].label = `${symbol} (${interval})`;
    priceChart.update();
  }

  window.ChartKit = { initPriceChart, updatePriceChart };
})();
