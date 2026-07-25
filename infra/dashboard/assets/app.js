import { buildStatsUrl, buildCsvUrl, requestJson } from './api.js';
import {
  toBarSeries,
  toLineSeries,
  topN,
  computeSuccessRatePercent,
  formatDuration,
  formatPercent,
  sumBy,
} from './charts.js';
import { drawBarChart, drawLineChart } from './rendering.js';

// Same-origin by default: the dashboard and the Worker are expected to sit
// behind the same Cloudflare Pages + Worker route once deployed. Override
// via `?api=https://...` only for local testing against a `wrangler dev`
// instance running on a different port.
const API_BASE = new URLSearchParams(location.search).get('api') || location.origin;

const CHART_DEFINITIONS = [
  { name: 'runs-per-day', title: 'Otimizações por dia', type: 'line', xKey: 'day', yKey: 'runs' },
  { name: 'os-versions', title: 'Versões do Windows', type: 'bar', labelKey: 'os_version', valueKey: 'runs' },
  { name: 'app-versions', title: 'Versões do FiveMCleaner', type: 'bar', labelKey: 'app_version', valueKey: 'runs' },
  { name: 'top-actions', title: 'Funções mais usadas', type: 'bar', labelKey: 'action_id', valueKey: 'uses' },
  { name: 'top-cpu', title: 'CPUs mais comuns', type: 'bar', labelKey: 'cpu_model', valueKey: 'runs' },
  { name: 'top-gpu', title: 'GPUs mais comuns', type: 'bar', labelKey: 'gpu_model', valueKey: 'runs' },
  { name: 'ram-buckets', title: 'Memória RAM', type: 'bar', labelKey: 'ram_bucket_gib', valueKey: 'runs' },
  { name: 'profiles', title: 'Perfis escolhidos', type: 'bar', labelKey: 'profile', valueKey: 'runs' },
];

async function main() {
  const loginView = document.getElementById('login-view');
  const dashboardView = document.getElementById('dashboard-view');
  const loginForm = document.getElementById('login-form');
  const loginError = document.getElementById('login-error');
  const logoutButton = document.getElementById('logout-button');
  const filterForm = document.getElementById('filter-form');

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
    };
  }

  async function fetchStat(name, filters) {
    const url = buildStatsUrl(API_BASE, name, filters);
    return requestJson(url);
  }

  async function refreshAll() {
    const filters = currentFilters();

    const [runsPerDay, successRate, averageTime, ...chartResults] = await Promise.all([
      fetchStat('runs-per-day', filters),
      fetchStat('success-rate', filters),
      fetchStat('average-time', filters),
      ...CHART_DEFINITIONS.map((definition) => fetchStat(definition.name, filters)),
    ]);

    if (runsPerDay.unauthorized || successRate.unauthorized || averageTime.unauthorized) {
      showLogin();
      return;
    }

    document.getElementById('tile-total-runs').textContent = sumBy(runsPerDay.data, 'runs');
    document.getElementById('tile-success-rate').textContent = formatPercent(
      computeSuccessRatePercent(successRate.data?.[0]),
    );
    document.getElementById('tile-average-time').textContent = formatDuration(averageTime.data?.[0]?.average_ms);

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
