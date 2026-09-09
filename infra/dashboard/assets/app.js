import { buildStatsUrl, buildCsvUrl, buildBugsUrl, buildBugsCsvUrl, buildUpdaterEventsUrl, requestJson, resolveApiBase, getCsrfToken, getLiveAlert, setLiveAlert } from './api.js';
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
  formatActionIds,
  limitFeedRows,
} from './charts.js';
import { drawBarChart, drawDonutChart, drawLineChart, DONUT_COLORS, CHART_COLORS } from './rendering.js';

// The dashboard (Cloudflare Pages) and the API are deliberately separate
// origins, so location.origin would point at the dashboard instead of the API.
// Override via `?api=https://...` only for local testing against a
// `wrangler dev` instance running on a different port.
//
// The override is the only piece of the URL an attacker can put in front of
// a victim in production (e.g. `?api=https://evil.example`), and the login
// form POSTs the admin password there. So it is honored only when the
// dashboard itself is served from localhost -- a production host always
// talks to the real Worker and ignores any `?api=`.
const DEFAULT_API_BASE = 'https://api.vemryx.com';
const API_BASE = resolveApiBase(DEFAULT_API_BASE, location.hostname, new URLSearchParams(location.search));

const CHART_DEFINITIONS = [
  { name: 'runs-per-day', type: 'line', xKey: 'day', yKey: 'runs', valueLabel: 'execuções', showPercent: false, color: CHART_COLORS[0] },
  { name: 'os-versions', type: 'donut', labelKey: 'os_version', valueKey: 'runs', valueLabel: 'eventos', legendId: 'legend-os-versions' },
  { name: 'app-versions', type: 'donut', labelKey: 'app_version', valueKey: 'runs', valueLabel: 'eventos', labelFormatter: formatAppVersion, legendId: 'legend-app-versions' },
  { name: 'top-cpu', type: 'bar', labelKey: 'cpu_model', valueKey: 'runs', valueLabel: 'eventos', horizontal: true, limit: 5, color: CHART_COLORS[1] },
  { name: 'top-gpu', type: 'bar', labelKey: 'gpu_model', valueKey: 'runs', valueLabel: 'eventos', horizontal: true, limit: 5, color: CHART_COLORS[0] },
  { name: 'ram-buckets', type: 'bar', labelKey: 'ram_bucket_gib', valueKey: 'runs', valueLabel: 'eventos', horizontal: true, limit: 5, color: CHART_COLORS[2] },
  { name: 'error-categories', type: 'donut', labelKey: 'error_category', valueKey: 'occurrences', valueLabel: 'falhas', legendId: 'legend-error-categories' },
  {
    name: 'errors-by-version',
    type: 'bar',
    combinedKeys: ['app_version', 'error_category'],
    valueKey: 'occurrences',
    valueLabel: 'falhas',
    horizontal: true,
    limit: 8,
    color: CHART_COLORS[4],
  },
  { name: 'bug-codes', type: 'bar', labelKey: 'bug_code', valueKey: 'occurrences', valueLabel: 'falhas', horizontal: true, limit: 8, color: CHART_COLORS[3] },
];

async function main() {
  let csrfToken = null;
  const loginView = document.getElementById('login-view');
  const dashboardView = document.getElementById('dashboard-view');
  const loginForm = document.getElementById('login-form');
  const loginError = document.getElementById('login-error');
  const logoutButton = document.getElementById('logout-button');
  const filterForm = document.getElementById('filter-form');
  const recentFailuresBody = document.getElementById('recent-failures-body');
  const recentFailuresCsvLink = document.getElementById('csv-recent-failures');
  const recentFailuresToggle = document.getElementById('recent-failures-toggle');
  const bugReportsBody = document.getElementById('bug-reports-body');
  const bugReportsCsvLink = document.getElementById('csv-bug-reports');
  const bugReportsToggle = document.getElementById('bug-reports-toggle');
  const updaterEventsBody = document.getElementById('updater-events-body');
  const updaterEventsToggle = document.getElementById('updater-events-toggle');
  const refreshStatus = document.getElementById('refresh-status');
  const liveAlertForm = document.getElementById('live-alert-form');
  const liveAlertMessage = document.getElementById('live-alert-message');
  const liveAlertCounter = document.getElementById('live-alert-counter');
  const liveAlertStatus = document.getElementById('live-alert-status');
  const liveAlertError = document.getElementById('live-alert-error');
  const liveAlertDeactivate = document.getElementById('live-alert-deactivate');
  const detailDialog = document.getElementById('detail-dialog');
  const detailDialogKind = document.getElementById('detail-dialog-kind');
  const detailDialogTitle = document.getElementById('detail-dialog-title');
  const detailDialogMeta = document.getElementById('detail-dialog-meta');
  const detailDialogContent = document.getElementById('detail-dialog-content');
  const detailDialogCopy = document.getElementById('detail-dialog-copy');
  let detailClipboardText = '';

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

    const body = await response.json().catch(() => null);
    if (typeof body?.csrfToken !== 'string') {
      loginError.textContent = 'Não foi possível iniciar a sessão com segurança.';
      return;
    }

    csrfToken = body.csrfToken;
    showDashboard();
    await Promise.all([refreshAll(), loadLiveAlertStatus()]);
  });

  logoutButton.addEventListener('click', async () => {
    try {
      await fetch(`${API_BASE}/admin/logout`, { method: 'POST', credentials: 'include' });
    } catch {
      // Best-effort: session will expire server-side if revoke fails
    }
    showLogin();
  });

  filterForm.addEventListener('submit', (event) => {
    event.preventDefault();
    refreshAll().catch(() => {
      refreshStatus.textContent = 'Erro ao atualizar dados';
    });
  });

  const LIVE_ALERT_MAX_LENGTH = 300;

  function updateLiveAlertCounter() {
    liveAlertCounter.textContent = `${liveAlertMessage.value.length}/${LIVE_ALERT_MAX_LENGTH}`;
  }

  liveAlertMessage.addEventListener('input', updateLiveAlertCounter);

  liveAlertForm.querySelectorAll('.chip').forEach((chip) => {
    chip.addEventListener('click', () => {
      liveAlertMessage.value = chip.dataset.template;
      updateLiveAlertCounter();
      liveAlertMessage.focus();
    });
  });

  function formatLiveAlertStatus(active, id) {
    if (!active) return 'Inativo';
    const when = id ? new Date(id) : null;
    const stamp = when && !Number.isNaN(when.getTime()) ? ` desde ${when.toLocaleString('pt-BR')}` : '';
    return `Ativo${stamp}`;
  }

  async function loadLiveAlertStatus() {
    const result = await getLiveAlert(API_BASE);
    if (result.error || result.unauthorized) {
      liveAlertStatus.textContent = 'Não foi possível carregar o status';
      liveAlertStatus.classList.remove('live-alert-status-active');
      return;
    }

    const { message, active, id } = result.data;
    liveAlertMessage.value = active ? message || '' : '';
    updateLiveAlertCounter();
    liveAlertStatus.textContent = formatLiveAlertStatus(active, id);
    liveAlertStatus.classList.toggle('live-alert-status-active', active);
  }

  liveAlertForm.addEventListener('submit', async (event) => {
    event.preventDefault();
    liveAlertError.textContent = '';
    const message = liveAlertMessage.value.trim();
    if (!message) {
      liveAlertError.textContent = 'Escreva uma mensagem antes de enviar.';
      return;
    }

    const result = await setLiveAlert(API_BASE, { message, active: true }, csrfToken);
    if (result.error || result.unauthorized) {
      liveAlertError.textContent = 'Erro ao enviar o aviso.';
      return;
    }

    await loadLiveAlertStatus();
  });

  liveAlertDeactivate.addEventListener('click', async () => {
    liveAlertError.textContent = '';
    const result = await setLiveAlert(API_BASE, { message: '', active: false }, csrfToken);
    if (result.error || result.unauthorized) {
      liveAlertError.textContent = 'Erro ao desativar o aviso.';
      return;
    }

    await loadLiveAlertStatus();
  });

  document.getElementById('detail-dialog-close').addEventListener('click', () => detailDialog.close());
  document.getElementById('detail-dialog-dismiss').addEventListener('click', () => detailDialog.close());
  detailDialog.addEventListener('click', (event) => {
    if (event.target === detailDialog) detailDialog.close();
  });
  detailDialogCopy.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(detailClipboardText);
      detailDialogCopy.textContent = 'Copiado';
    } catch {
      detailDialogCopy.textContent = 'Falha ao copiar';
    }
    setTimeout(() => { detailDialogCopy.textContent = 'Copiar detalhes'; }, 1500);
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

  function renderTableBody(tbody, rows, mapRow, colspan, cellClass) {
    tbody.innerHTML = '';
    if (!rows || rows.length === 0) {
      tbody.innerHTML = `<tr><td colspan="${colspan}" class="empty-row">Sem dados ainda</td></tr>`;
      return;
    }
    for (const row of rows) {
      const tr = document.createElement('tr');
      for (const [index, value] of mapRow(row).entries()) {
        const td = document.createElement('td');
        td.textContent = value;
        if (cellClass) td.className = cellClass(index);
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
  }

  function renderDetailedTable(tbody, rows, mapRow, colspan, openDetails, cellClass) {
    tbody.replaceChildren();
    if (!rows?.length) {
      const cell = document.createElement('td');
      cell.colSpan = colspan;
      cell.className = 'empty-row';
      cell.textContent = 'Sem dados ainda';
      const row = document.createElement('tr');
      row.appendChild(cell);
      tbody.appendChild(row);
      return;
    }

    rows.forEach((source, rowIndex) => {
      const row = document.createElement('tr');
      mapRow(source).forEach((value, cellIndex) => {
        const cell = document.createElement('td');
        cell.textContent = value;
        if (cellClass) cell.className = cellClass(cellIndex);
        row.appendChild(cell);
      });
      const actionCell = document.createElement('td');
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'details-button';
      button.textContent = 'Ver';
      button.setAttribute('aria-label', `Ver detalhes da linha ${rowIndex + 1}`);
      button.addEventListener('click', () => openDetails(source));
      actionCell.appendChild(button);
      row.appendChild(actionCell);
      tbody.appendChild(row);
    });
  }

  function renderRecentFailures(rows) {
    renderDetailedTable(recentFailuresBody, rows, toRecentFailureRow, 7, openFailureDetails,
      (index) => index === 1 ? 'failure-code' : '');
  }

  function renderBugReports(rows) {
    renderDetailedTable(bugReportsBody, rows, toBugReportRow, 6, openBugReportDetails,
      (index) => index === 1 ? 'failure-code' : index === 2 ? 'summary-cell' : '');
  }

  function renderUpdaterEvents(rows) {
    renderTableBody(updaterEventsBody, rows, toUpdaterEventRow, 8);
  }

  const feeds = {
    recentFailures: { rows: [], expanded: false, button: recentFailuresToggle, render: renderRecentFailures, label: 'eventos recentes' },
    bugReports: { rows: [], expanded: false, button: bugReportsToggle, render: renderBugReports, label: 'bugs reportados' },
    updaterEvents: { rows: [], expanded: false, button: updaterEventsToggle, render: renderUpdaterEvents, label: 'bugs do updater' },
  };

  function renderFeed(name) {
    const feed = feeds[name];
    feed.render(limitFeedRows(feed.rows, feed.expanded));
    const canToggle = feed.rows.length > 5;
    feed.button.hidden = !canToggle;
    feed.button.textContent = feed.expanded ? '−' : '+';
    feed.button.title = feed.expanded ? 'Mostrar somente os 5 mais recentes' : 'Mostrar todos';
    feed.button.setAttribute('aria-label', `${feed.expanded ? 'Mostrar somente os 5 mais recentes' : 'Mostrar todos os'} ${feed.label}`);
    feed.button.setAttribute('aria-expanded', String(feed.expanded));
  }

  function setFeedRows(name, rows) {
    feeds[name].rows = rows ?? [];
    renderFeed(name);
  }

  Object.entries(feeds).forEach(([name, feed]) => {
    feed.button.addEventListener('click', () => {
      feed.expanded = !feed.expanded;
      renderFeed(name);
    });
  });

  function openFailureDetails(row) {
    const actions = formatActionIds(row.action_ids);
    showDetails({
      kind: 'Detalhes do erro',
      title: row.bug_code || 'Falha sem código específico',
      meta: [
        ['Ocorrência', displayValue(row.event_id)],
        ['Recebido em', toRecentFailureRow(row)[0]],
        ['Versão', formatAppVersion(row.app_version)],
        ['Ambiente', displayValue(row.environment)],
        ['Perfil', displayValue(row.profile)],
      ],
      sections: [
        ['Código da falha', displayValue(row.bug_code)],
        ['Categoria', displayValue(row.error_category)],
        ['Tempo de execução', formatDuration(row.execution_time_ms)],
        ['Contexto técnico', [
          `Windows: ${displayValue(row.os_version)}`,
          `Arquitetura: ${displayValue(row.system_architecture)}`,
          `CPU: ${displayValue(row.cpu_model)}`,
          `GPU: ${displayValue(row.gpu_model)}`,
          `Ações no plano: ${displayValue(row.optimization_target_count)}`,
        ].join('\n')],
        ['IDs das ações no plano', actions.length ? actions : ['Não informado pela telemetria opcional.']],
      ],
    });
  }

  function openBugReportDetails(row) {
    showDetails({
      kind: 'Detalhes do relato',
      title: row.bug_code || 'Relato sem código específico',
      meta: [
        ['Ocorrência', displayValue(row.report_id)],
        ['Recebido em', toBugReportRow(row)[0]],
        ['Versão', formatAppVersion(row.app_version)],
        ['Ambiente', displayValue(row.environment)],
        ['Perfil', displayValue(row.profile)],
      ],
      sections: [
        ['Resumo', displayValue(row.summary)],
        ['Descrição', displayValue(row.description)],
        ['Contexto técnico', displayValue(row.technical_summary)],
        ['E-mail', displayValue(row.email)],
        ['Trecho de log', row.log_text || 'Nenhum trecho de log enviado.', 'code'],
      ],
    });
  }

  function showDetails({ kind, title, meta, sections }) {
    detailDialogKind.textContent = kind;
    detailDialogTitle.textContent = title;
    detailDialogMeta.replaceChildren(...meta.map(([label, value]) => {
      const item = document.createElement('div');
      const term = document.createElement('dt');
      term.textContent = label;
      const description = document.createElement('dd');
      description.textContent = value;
      description.title = value;
      item.append(term, description);
      return item;
    }));

    detailDialogContent.replaceChildren(...sections.map(([heading, value, style]) => {
      const section = document.createElement('section');
      section.className = 'detail-section';
      const titleElement = document.createElement('h3');
      titleElement.textContent = heading;
      let content;
      if (Array.isArray(value)) {
        content = document.createElement('ul');
        content.append(...value.map((item) => {
          const listItem = document.createElement('li');
          listItem.textContent = item;
          return listItem;
        }));
      } else {
        content = document.createElement(style === 'code' ? 'pre' : 'p');
        content.textContent = value;
      }
      section.append(titleElement, content);
      return section;
    }));

    detailClipboardText = [
      `${kind}: ${title}`,
      ...meta.map(([label, value]) => `${label}: ${value}`),
      ...sections.map(([heading, value]) => `${heading}:\n${Array.isArray(value) ? value.join('\n') : value}`),
    ].join('\n\n');
    detailDialog.showModal();
  }

  function displayValue(value) {
    return value === null || value === undefined || value === '' ? '—' : String(value);
  }

  function renderLegend(id, series) {
    const container = document.getElementById(id);
    if (!container) return;
    const colors = DONUT_COLORS;
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
      value.textContent = `${point.value} · ${Math.round(point.percent * 10) / 10}%`;
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

    setFeedRows('bugReports', bugReports.unauthorized || bugReports.error ? [] : bugReports.data);
    bugReportsCsvLink.href = buildBugsCsvUrl(API_BASE, filters);
    setFeedRows('updaterEvents', updaterEvents.unauthorized || updaterEvents.error ? [] : updaterEvents.data);

    document.getElementById('tile-total-runs').textContent = sumBy(runsPerDay.data, 'runs');
    document.getElementById('tile-success-rate').textContent = formatPercent(
      computeSuccessRatePercent(successRate.data?.[0]),
    );
    document.getElementById('tile-average-time').textContent = formatDuration(averageTime.data?.[0]?.average_ms);
    document.getElementById('tile-total-failures').textContent = errorCategories.unauthorized || errorCategories.error
      ? '—'
      : sumBy(errorCategories.data, 'occurrences');

    setFeedRows('recentFailures', recentFailures.unauthorized || recentFailures.error ? [] : recentFailures.data);
    recentFailuresCsvLink.href = buildCsvUrl(API_BASE, 'recent-failures', filters);

    CHART_DEFINITIONS.forEach((definition, index) => {
      const result = chartResults[index];
      const canvas = document.getElementById(`chart-${definition.name}`);
      const csvLink = document.getElementById(`csv-${definition.name}`);
      if (csvLink) csvLink.href = buildCsvUrl(API_BASE, definition.name, filters);

      if (!canvas || result.unauthorized) return;

      const options = {
        horizontal: definition.horizontal,
        color: definition.color,
        valueLabel: definition.valueLabel,
        showPercent: definition.showPercent,
      };
      const data = result.error ? [] : result.data;

      if (definition.type === 'line') {
        drawLineChart(canvas, toLineSeries(data, definition.xKey, definition.yKey), options);
      } else if (definition.type === 'donut') {
        const series = topN(toBarSeries(data, definition.labelKey, definition.valueKey), 5);
        const formatted = definition.labelFormatter ? series.map((point) => ({ ...point, label: definition.labelFormatter(point.label) })) : series;
        drawDonutChart(canvas, formatted, options);
        renderLegend(definition.legendId, formatted);
      } else if (definition.combinedKeys) {
        drawBarChart(
          canvas,
          topN(toCombinedBarSeries(data.map((row) => ({ ...row, app_version: formatAppVersion(row.app_version) })), definition.combinedKeys, definition.valueKey), definition.limit ?? 10),
          options,
        );
      } else {
        const series = topN(toBarSeries(data, definition.labelKey, definition.valueKey), definition.limit ?? 10);
        drawBarChart(canvas, definition.labelFormatter ? series.map((point) => ({ ...point, label: definition.labelFormatter(point.label) })) : series, options);
      }
    });
    const failedRequests = [runsPerDay, successRate, averageTime, errorCategories, recentFailures, bugReports, updaterEvents, ...chartResults]
      .filter((result) => result.error).length;
    refreshStatus.textContent = failedRequests ? `Atualizado com ${failedRequests} fonte(s) indisponível(is)` : 'Dados atualizados';
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
    const csrf = await getCsrfToken(API_BASE);
    if (csrf.unauthorized) {
      showLogin();
      return;
    }
    if (csrf.error || typeof csrf.data?.csrfToken !== 'string') {
      showLogin();
      loginError.textContent = 'Não foi possível iniciar a sessão com segurança.';
      return;
    }

    csrfToken = csrf.data.csrfToken;
    showDashboard();
    await Promise.all([refreshAll(), loadLiveAlertStatus()]);
  }
}

main().catch((error) => {
  console.error('Dashboard initialization failed:', error);
  const loginError = document.getElementById('login-error');
  if (loginError) loginError.textContent = 'Erro ao iniciar o dashboard. Recarregue a página.';
});
