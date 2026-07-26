import { buildStatsUrl, buildCsvUrl, buildBugsUrl, requestJson } from './api.js';
import {
  toBarSeries,
  toCombinedBarSeries,
  toLineSeries,
  topN,
  computeSuccessRatePercent,
  formatDuration,
  formatPercent,
  sumBy,
  toRecentFailureRow,
  toBugReportRow,
} from './charts.js';
import { drawBarChart, drawLineChart } from './rendering.js';

// The dashboard (Cloudflare Pages) and the Worker are deliberately two
// separate origins -- no custom domain/routing was set up to make them
// share one, so the deployed Worker's own workers.dev URL is the default.
// Override via `?api=https://...` only for local testing against a
// `wrangler dev` instance running on a different port.
const DEFAULT_API_BASE = 'https://fivemcleaner-telemetry.felipemarquesini10.workers.dev';
const API_BASE = new URLSearchParams(location.search).get('api') || DEFAULT_API_BASE;

const CHART_DEFINITIONS = [
  { name: 'runs-per-day', title: 'Otimizações por dia', type: 'line', xKey: 'day', yKey: 'runs' },
  { name: 'os-versions', title: 'Versões do Windows', type: 'bar', labelKey: 'os_version', valueKey: 'runs' },
  { name: 'app-versions', title: 'Versões do FiveMCleaner', type: 'bar', labelKey: 'app_version', valueKey: 'runs' },
  { name: 'profiles', title: 'Perfis escolhidos', type: 'bar', labelKey: 'profile', valueKey: 'runs' },
  { name: 'top-actions', title: 'Funções mais usadas', type: 'bar', labelKey: 'action_id', valueKey: 'uses' },
  { name: 'top-cpu', title: 'CPUs mais comuns', type: 'bar', labelKey: 'cpu_model', valueKey: 'runs' },
  { name: 'top-gpu', title: 'GPUs mais comuns', type: 'bar', labelKey: 'gpu_model', valueKey: 'runs' },
  { name: 'ram-buckets', title: 'Memória RAM', type: 'bar', labelKey: 'ram_bucket_gib', valueKey: 'runs' },
  { name: 'error-categories', title: 'Erros por categoria', type: 'bar', labelKey: 'error_category', valueKey: 'occurrences' },
  { name: 'top-actions-in-failures', title: 'Ações associadas a falhas', type: 'bar', labelKey: 'action_id', valueKey: 'failures' },
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

    const response = await fetch(`${API_BASE}/admin/login`, {
      method: 'POST',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ password }),
    });

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

  async function refreshAll() {
    const filters = currentFilters();

    const [runsPerDay, successRate, averageTime, errorCategories, recentFailures, bugReports, ...chartResults] =
      await Promise.all([
        fetchStat('runs-per-day', filters),
        fetchStat('success-rate', filters),
        fetchStat('average-time', filters),
        fetchStat('error-categories', filters),
        fetchStat('recent-failures', filters),
        requestJson(buildBugsUrl(API_BASE, filters)),
        ...CHART_DEFINITIONS.map((definition) => fetchStat(definition.name, filters)),
      ]);

    if (runsPerDay.unauthorized || successRate.unauthorized || averageTime.unauthorized) {
      showLogin();
      return;
    }

    renderBugReports(bugReports.unauthorized || bugReports.error ? [] : bugReports.data);

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
      } else if (definition.combinedKeys) {
        drawBarChart(
          canvas,
          topN(toCombinedBarSeries(result.data, definition.combinedKeys, definition.valueKey), 10),
        );
      } else {
        drawBarChart(canvas, topN(toBarSeries(result.data, definition.labelKey, definition.valueKey), 10));
      }
    });
  }

  // Probe whether a session already exists (e.g. the page was reloaded)
  // instead of always forcing a fresh login.
  const probe = await fetchStat('success-rate', {});
  if (probe.unauthorized) {
    showLogin();
  } else {
    showDashboard();
    await refreshAll();
  }
}

main();
