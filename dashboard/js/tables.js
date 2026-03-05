/* global window, document */
(function () {
  function esc(v) {
    return String(v ?? "").replace(/[&<>"']/g, (m) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[m]));
  }

  function fmtNum(v, d = 4) {
    if (v === null || v === undefined || Number.isNaN(Number(v))) return "-";
    return Number(v).toLocaleString(undefined, { maximumFractionDigits: d, minimumFractionDigits: d });
  }

  function fmtDate(v) {
    if (!v) return "-";
    return new Date(v).toLocaleString();
  }

  function fmtDuration(start, end) {
    if (!start || !end) return "-";
    const ms = new Date(end) - new Date(start);
    if (ms <= 0) return "-";
    const min = Math.floor(ms / 60000);
    const h = Math.floor(min / 60);
    const m = min % 60;
    return `${h}h ${m}m`;
  }

  function buildSortableTable(containerId, columns, rows) {
    const mount = document.getElementById(containerId);
    if (!mount) return;

    const getCell = (row, key) => row[key];
    const tableId = `${containerId}-table`;
    const html = `
      <div class="table-wrap">
        <table id="${tableId}">
          <thead><tr>${columns.map((c) => `<th data-key="${esc(c.key)}">${esc(c.label)}</th>`).join("")}</tr></thead>
          <tbody>
            ${rows.map((r) => `<tr>${columns.map((c) => `<td>${c.render ? c.render(r) : esc(getCell(r, c.key))}</td>`).join("")}</tr>`).join("")}
          </tbody>
        </table>
      </div>`;
    mount.innerHTML = html;

    const table = document.getElementById(tableId);
    const headers = table.querySelectorAll("thead th");
    headers.forEach((h) => {
      h.addEventListener("click", () => {
        const key = h.dataset.key;
        const asc = h.dataset.asc !== "true";
        h.dataset.asc = asc ? "true" : "false";
        rows.sort((a, b) => {
          const av = a[key];
          const bv = b[key];
          if (typeof av === "number" && typeof bv === "number") return asc ? av - bv : bv - av;
          return asc
            ? String(av ?? "").localeCompare(String(bv ?? ""))
            : String(bv ?? "").localeCompare(String(av ?? ""));
        });
        buildSortableTable(containerId, columns, rows);
      });
    });
  }

  window.TableKit = { buildSortableTable, fmtNum, fmtDate, fmtDuration, esc };
})();
