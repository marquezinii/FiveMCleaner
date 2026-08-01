import { buildStatsUrl, buildCsvUrl, buildBugsUrl, buildUpdaterEventsUrl, requestJson, resolveApiBase } from './api.js';
import {
  toBarSeries,
  toCombinedBarSeries,
  toLineSeries,
  topN,
  computeSuccessRatePercent,
  formatDuration,
  formatPercent,
  formatAppVersion,
  toDistributionRows,
  sumBy,
  toRecentFailureRow,
  toBugReportRow,
  toUpdaterEventRow,
} from './charts.js';
import { drawBarChart, drawDonutChart, drawLineChart } from './rendering.js';

// The dashboard (Cloudflare Pages) and the Worker are deliberately two
// separate origins -- no custom domain/routing was set up to make them
// share one, so the deployed Worker's own workers.dev URL is the default.
// Override via `?api=https://...` only for local testing against a
// `wrangler dev` instance running on a different port.
//
// The override is the only piece of the URL an attacker can put in front of
// a victim in production (e.g. `?api=https://evil.example`), and the login
// form POSTs the admin password there. So it is honored only when the
// dashboard itself is served from localhost -- a production host always
// talks to the real Worker and ignores any `?api=`.
const DEFAULT_API_BASE = 'https://fivemcleaner-telemetry.felipemarquesini10.workers.dev';
const API_BASE = resolveApiBase(DEFAULT_API_BASE, location.hostname, new URLSearchParams(location.search));

const CHART_DEFINITIONS = [
  { name: 'runs-per-day', title: 'Otimizações por dia', type: 'line', xKey: 'day', yKey: 'runs' },
  { name: 'os-versions', type: 'donut', labelKey: 'os_version', valueKey: 'runs', legendId: 'legend-os-versions' },
  { name: 'app-versions', type: 'donut', labelKey: 'app_version', valueKey: 'runs', labelFormatter: formatAppVersion, legendId: 'legend-app-versions' },
  { name: 'top-cpu', type: 'bar', labelKey: 'cpu_model', valueKey: 'runs', horizontal: true },
  { name: 'top-gpu', type: 'bar', labelKey: 'gpu_model', valueKey: 'runs', horizontal: true },
  { name: 'ram-buckets', type: 'bar', labelKey: 'ram_bucket_gib', valueKey: 'runs', horizontal: true },
  { name: 'error-categories', type: 'donut', labelKey: 'error_category', valueKey: 'occurrences', legendId: 'legend-error-categories' },
  {
    name: 'errors-by-version',
    title: 'Erros por versão',
    type: 'bar',
    combinedKeys: ['app_version', 'error_category'],
    valueKey: 'occurrences',
  },
];

async function main() {
  const loginView = document.getElementById('login-view');
  const dashboardView = document.getElementById('dashboard-view');
  const loginForm = document.getElementById('login-form');
  const loginError = document.getElementById('login-error');
  const logoutButton = document.getElementById('logout-button');
  const filterForm = document.getElementById('filter-form');
  const recentFailuresBody = document.getElementById('recent-failures-body');
  const recentFailuresCsvLink = document.getElementById('csv-recent-failures');
  const bugReportsBody = document.getElementById('bug-reports-body');
  const updaterEventsBody = document.getElementById('updater-events-body');
  const refreshStatus = document.getElementById('refresh-status');

  function showLogin() {
    loginView.classList.remove('hidden');
    dashboardView.classList.add('hidden');
  }

  function showDashboard() {
    loginView.classList.add('hidden');
    dashboardView.classList.remove('hidden');
  }

  loginForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    loginError.textContent = '';
    const password = new FormData(loginForm).get('password');

    let response;
    try {
      response = await fetch(`${API_BASE}/admin/login`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ password }),
      });
    } catch {
      loginError.textContent = 'Não foi possível conectar à telemetria. Verifique se o Worker está no ar.';
      return;
    }

    if (response.status === 429) {
      loginError.textContent = 'Muitas tentativas. Tente novamente mais tarde.';
      return;
    }

    if (!response.ok) {
      loginError.textContent = 'Senha incorreta.';
      return;
    }

    showDashboard();
    await refreshAll();
  });

  logoutButton.addEventListener('click', async () => {
    await fetch(`${API_BASE}/admin/logout`, { method: 'POST', credentials: 'include' });
    showLogin();
  });

  filterForm.addEventListener('submit', (event) => {
    event.preventDefault();
    refreshAll();
  });

  function currentFilters() {
    const data = new FormData(filterForm);
    return {
      from: data.get('from') || undefined,
      to: data.get('to') || undefined,
      version: data.get('version') || undefined,
      environment: data.get('environment') || undefined,
    };
  }

  async function fetchStat(name, filters) {
    const url = buildStatsUrl(API_BASE, name, filters);
    return requestJson(url);
  }

  function renderRecentFailures(rows) {
    recentFailuresBody.innerHTML = '';
    if (!rows || rows.length === 0) {
      recentFailuresBody.innerHTML = '<tr><td colspan="8" class="empty-row">Sem dados ainda</td></tr>';
      return;
    }

    for (const row of rows) {
      const cells = toRecentFailureRow(row);
      const tr = document.createElement('tr');
      cells.forEach((value, index) => {
        const td = document.createElement('td');
        td.textContent = value;
        if (index === 1) {
          td.classList.add('error-category');
        }

        tr.appendChild(td);
      });
      recentFailuresBody.appendChild(tr);
    }
  }

  function renderBugReports(rows) {
    bugReportsBody.innerHTML = '';
    if (!rows || rows.length === 0) {
      bugReportsBody.innerHTML = '<tr><td colspan="8" class="empty-row">Sem dados ainda</td></tr>';
      return;
    }

    for (const row of rows) {
      const cells = toBugReportRow(row);
      const tr = document.createElement('tr');
      cells.forEach((value) => {
        const td = document.createElement('td');
        td.textContent = value;
        tr.appendChild(td);
      });
      bugReportsBody.appendChild(tr);
    }
  }

  function renderUpdaterEvents(rows) {
    updaterEventsBody.innerHTML = '';
    if (!rows || rows.length === 0) {
      updaterEventsBody.innerHTML = '<tr><td colspan="7" class="empty-row">Sem dados ainda</td></tr>';
      return;
    }
    for (const row of rows) {
      const tr = document.createElement('tr');
      for (const value of toUpdaterEventRow(row)) {
        const td = document.createElement('td');
        td.textContent = value;
        tr.appendChild(td);
      }
      updaterEventsBody.appendChild(tr);
    }
  }

  function renderLegend(id, series) {
    const container = document.getElementById(id);
    if (!container) return;
    const colors = ['#ff7a18', '#88a8dc', '#78d497', '#a67bd6', '#f4be55'];
    container.replaceChildren(...toDistributionRows(series).map((point, index) => {
      const row = document.createElement('div');
      row.className = 'legend-row';
      const swatch = document.createElement('span');
      swatch.className = 'legend-swatch';
      swatch.style.backgroundColor = colors[index % colors.length];
      const label = document.createElement('span');
      label.className = 'legend-name';
      label.textContent = point.label;
      const value = document.createElement('span');
      value.className = 'legend-value';
      value.textContent = `${Math.round(point.percent * 10) / 10}%`;
      row.append(swatch, label, value);
      return row;
    }));
  }

  async function refreshAll() {
    const filters = currentFilters();
    refreshStatus.textContent = 'Atualizando dados…';

    const [runsPerDay, successRate, averageTime, errorCategories, recentFailures, bugReports, updaterEvents, ...chartResults] =
      await Promise.all([
        fetchStat('runs-per-day', filters),
        fetchStat('success-rate', filters),
        fetchStat('average-time', filters),
        fetchStat('error-categories', filters),
        fetchStat('recent-failures', filters),
        requestJson(buildBugsUrl(API_BASE, filters)),
        requestJson(buildUpdaterEventsUrl(API_BASE, filters)),
        ...CHART_DEFINITIONS.map((definition) => fetchStat(definition.name, filters)),
      ]);

    if (runsPerDay.unauthorized || successRate.unauthorized || averageTime.unauthorized) {
      showLogin();
      return;
    }

    renderBugReports(bugReports.unauthorized || bugReports.error ? [] : bugReports.data);
    renderUpdaterEvents(updaterEvents.unauthorized || updaterEvents.error ? [] : updaterEvents.data);

    document.getElementById('tile-total-runs').textContent = sumBy(runsPerDay.data, 'runs');
    document.getElementById('tile-success-rate').textContent = formatPercent(
      computeSuccessRatePercent(successRate.data?.[0]),
    );
    document.getElementById('tile-average-time').textContent = formatDuration(averageTime.data?.[0]?.average_ms);
    document.getElementById('tile-total-failures').textContent = errorCategories.unauthorized
      ? '—'
      : sumBy(errorCategories.data, 'occurrences');

    renderRecentFailures(recentFailures.unauthorized ? [] : recentFailures.data);
    recentFailuresCsvLink.href = buildCsvUrl(API_BASE, 'recent-failures', filters);

    CHART_DEFINITIONS.forEach((definition, index) => {
      const result = chartResults[index];
      const canvas = document.getElementById(`chart-${definition.name}`);
      const csvLink = document.getElementById(`csv-${definition.name}`);
      csvLink.href = buildCsvUrl(API_BASE, definition.name, filters);

      if (!canvas || result.unauthorized || result.error) {
        return;
      }

      if (definition.type === 'line') {
        drawLineChart(canvas, toLineSeries(result.data, definition.xKey, definition.yKey));
      } else if (definition.type === 'donut') {
        const series = topN(toBarSeries(result.data, definition.labelKey, definition.valueKey), 5);
        const formatted = definition.labelFormatter ? series.map((point) => ({ ...point, label: definition.labelFormatter(point.label) })) : series;
        drawDonutChart(canvas, formatted);
        renderLegend(definition.legendId, formatted);
      } else if (definition.combinedKeys) {
        drawBarChart(
          canvas,
          topN(toCombinedBarSeries(result.data.map((row) => ({ ...row, app_version: formatAppVersion(row.app_version) })), definition.combinedKeys, definition.valueKey), 10),
        );
      } else {
        const series = topN(toBarSeries(result.data, definition.labelKey, definition.valueKey), 10);
        drawBarChart(canvas, definition.labelFormatter ? series.map((point) => ({ ...point, label: definition.labelFormatter(point.label) })) : series, { horizontal: definition.horizontal });
      }
    });
    refreshStatus.textContent = 'Dados atualizados';
  }

  // Probe whether a session already exists (e.g. the page was reloaded)
  // instead of always forcing a fresh login.
  const probe = await fetchStat('success-rate', {});
  if (probe.unauthorized) {
    showLogin();
  } else if (probe.error) {
    // The Worker is unreachable -- show the login view with a message instead
    // of rendering an empty dashboard that only says "Sem dados ainda".
    showLogin();
    loginError.textContent = 'Não foi possível conectar à telemetria. Verifique se o Worker está no ar.';
  } else {
    showDashboard();
    await refreshAll();
  }
}

main();
