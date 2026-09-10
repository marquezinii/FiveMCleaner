import {
  buildStatsUrl,
  buildCsvUrl,
  buildBugsUrl,
  buildBugsCsvUrl,
  buildUpdaterEventsUrl,
  requestJson,
  resolveApiBase,
  getCsrfToken,
  getLiveAlert,
  setLiveAlert,
} from './api.js';
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

const DEFAULT_API_BASE = 'https://api.vemryx.com';
const API_BASE = resolveApiBase(DEFAULT_API_BASE, location.hostname, new URLSearchParams(location.search));
const number = new Intl.NumberFormat('pt-BR');
const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' });
const usd = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'USD', minimumFractionDigits: 4 });

const CHARTS = [
  { name: 'runs-per-day', type: 'line', x: 'day', y: 'runs', label: 'execuções', color: CHART_COLORS[0] },
  { name: 'accounts-per-day', type: 'line', x: 'day', y: 'accounts', label: 'contas', color: CHART_COLORS[1] },
  { name: 'ai-per-day', type: 'line', x: 'day', y: 'requests', label: 'requisições', color: CHART_COLORS[2] },
  { name: 'app-versions', type: 'donut', key: 'app_version', value: 'runs', label: 'eventos', format: formatAppVersion, legend: 'legend-app-versions' },
  { name: 'billing-subscriptions', type: 'donut', key: 'state', value: 'subscriptions', label: 'assinaturas', legend: 'legend-billing-subscriptions' },
  { name: 'profiles', type: 'donut', key: 'profile', value: 'runs', label: 'execuções', legend: 'legend-profiles' },
  { name: 'outcomes', type: 'donut', key: 'event_name', value: 'occurrences', label: 'eventos', format: formatOutcome, legend: 'legend-outcomes' },
  { name: 'error-categories', type: 'donut', key: 'error_category', value: 'occurrences', label: 'falhas', legend: 'legend-error-categories' },
  { name: 'actions', type: 'bar', key: 'action_id', value: 'runs', label: 'execuções', horizontal: true, limit: 10, color: CHART_COLORS[0] },
  { name: 'errors-by-version', type: 'bar', keys: ['app_version', 'error_category'], value: 'occurrences', label: 'falhas', horizontal: true, limit: 8, color: CHART_COLORS[4] },
  { name: 'bug-codes', type: 'bar', key: 'bug_code', value: 'occurrences', label: 'falhas', horizontal: true, limit: 8, color: CHART_COLORS[3] },
  { name: 'os-versions', type: 'donut', key: 'os_version', value: 'runs', label: 'eventos', legend: 'legend-os-versions' },
  { name: 'top-cpu', type: 'bar', key: 'cpu_model', value: 'runs', label: 'eventos', horizontal: true, limit: 6, color: CHART_COLORS[1] },
  { name: 'top-gpu', type: 'bar', key: 'gpu_model', value: 'runs', label: 'eventos', horizontal: true, limit: 6, color: CHART_COLORS[0] },
];

const SUMMARY_STATS = [
  'success-rate',
  'average-time',
  'account-summary',
  'ai-summary',
  'billing-payments',
  'updater-summary',
  'app-initializations-per-day',
  'abandoned-optimizations',
  'gtav-benchmark-outcomes',
  'reliability-by-version',
  'recent-failures',
];

async function main() {
  let csrfToken = null;
  const loginView = byId('login-view');
  const dashboardView = byId('dashboard-view');
  const loginForm = byId('login-form');
  const loginError = byId('login-error');
  const filterForm = byId('filter-form');
  const refreshStatus = byId('refresh-status');
  const dashboardMain = byId('dashboard-main');
  const detailDialog = byId('detail-dialog');
  let detailClipboardText = '';

  setPeriod(30);
  wireNavigation();

  function showLogin(message = '') {
    loginView.classList.remove('hidden');
    dashboardView.classList.add('hidden');
    loginError.textContent = message;
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
      showLogin('Não foi possível conectar ao Worker administrativo.');
      return;
    }

    if (response.status === 429) {
      showLogin('Muitas tentativas. Aguarde antes de tentar novamente.');
      return;
    }
    if (!response.ok) {
      showLogin('Senha incorreta.');
      return;
    }

    const body = await response.json().catch(() => null);
    if (typeof body?.csrfToken !== 'string') {
      showLogin('Não foi possível iniciar a sessão com segurança.');
      return;
    }

    csrfToken = body.csrfToken;
    loginForm.reset();
    showDashboard();
    await Promise.all([refreshAll(), loadLiveAlert()]);
  });

  byId('logout-button').addEventListener('click', async () => {
    try {
      await fetch(`${API_BASE}/admin/logout`, { method: 'POST', credentials: 'include' });
    } catch {
      // Session expiry remains the server-side fallback.
    }
    csrfToken = null;
    showLogin();
  });

  filterForm.addEventListener('submit', (event) => {
    event.preventDefault();
    refreshAll();
  });
  byId('refresh-button').addEventListener('click', refreshAll);
  filterForm.querySelectorAll('[data-days]').forEach((button) => button.addEventListener('click', () => {
    setPeriod(Number(button.dataset.days));
    filterForm.querySelectorAll('[data-days]').forEach((item) => item.classList.toggle('active', item === button));
    refreshAll();
  }));
  filterForm.querySelectorAll('input[type="date"]').forEach((input) => input.addEventListener('change', () => {
    filterForm.querySelectorAll('[data-days]').forEach((item) => item.classList.remove('active'));
  }));

  const feeds = createFeeds(detailDialog, (value) => { detailClipboardText = value; });
  byId('incident-search').addEventListener('input', () => feeds.render('recentFailures'));

  const alertMessage = byId('live-alert-message');
  const alertPreview = byId('live-alert-preview');
  const alertError = byId('live-alert-error');
  const alertCounter = byId('live-alert-counter');
  const alertSeverity = byId('live-alert-severity');

  function updateAlertDraft() {
    alertCounter.textContent = `${alertMessage.value.length}/300`;
    alertPreview.textContent = alertMessage.value.trim() || 'A mensagem aparecerá aqui.';
  }
  alertMessage.addEventListener('input', updateAlertDraft);
  document.querySelectorAll('.live-alert-templates .chip').forEach((chip) => chip.addEventListener('click', () => {
    alertMessage.value = chip.dataset.template;
    alertSeverity.value = chip.dataset.severity || 'important';
    updateAlertDraft();
    alertMessage.focus();
  }));

  function formatLiveAlertStatus(active, id, severity) {
    if (!active) return 'Inativo';
    const label = { info: 'Informativo', important: 'Importante', critical: 'Crítico' }[severity] || 'Importante';
    const createdAt = id ? new Date(id) : null;
    const stamp = createdAt && !Number.isNaN(createdAt.getTime())
      ? ` desde ${createdAt.toLocaleString('pt-BR')}`
      : '';
    return `Ativo · ${label}${stamp}`;
  }

  async function loadLiveAlert() {
    const result = await getLiveAlert(API_BASE);
    const status = byId('live-alert-status');
    if (result.error || result.unauthorized) {
      status.textContent = 'Indisponível';
      status.classList.remove('active');
      return;
    }
    const { active, id, message, severity } = result.data ?? {};
    status.textContent = formatLiveAlertStatus(active, id, severity);
    status.classList.toggle('active', active);
    status.dataset.severity = active ? severity || 'important' : '';
    alertMessage.value = active ? message || '' : '';
    alertSeverity.value = severity || 'important';
    updateAlertDraft();
  }

  byId('live-alert-form').addEventListener('submit', async (event) => {
    event.preventDefault();
    alertError.textContent = '';
    const message = alertMessage.value.trim();
    if (!message) {
      alertError.textContent = 'Escreva a mensagem antes de publicar.';
      return;
    }
    if (!confirm(`Publicar este aviso para todos os aplicativos ativos?\n\n${message}`)) return;
    const result = await setLiveAlert(API_BASE, { message, active: true, severity: alertSeverity.value }, csrfToken);
    if (result.unauthorized) return showLogin('Sua sessão expirou.');
    if (result.error) {
      alertError.textContent = 'O aviso não foi publicado. Tente novamente.';
      return;
    }
    await loadLiveAlert();
  });

  byId('live-alert-deactivate').addEventListener('click', async () => {
    alertError.textContent = '';
    if (!confirm('Desativar o aviso ao vivo atual?')) return;
    const result = await setLiveAlert(API_BASE, { active: false, severity: alertSeverity.value }, csrfToken);
    if (result.unauthorized) return showLogin('Sua sessão expirou.');
    if (result.error) {
      alertError.textContent = 'O aviso não foi desativado.';
      return;
    }
    await loadLiveAlert();
  });

  byId('detail-dialog-close').addEventListener('click', () => detailDialog.close());
  byId('detail-dialog-dismiss').addEventListener('click', () => detailDialog.close());
  detailDialog.addEventListener('click', (event) => {
    if (event.target === detailDialog) detailDialog.close();
  });
  byId('detail-dialog-copy').addEventListener('click', async () => {
    const button = byId('detail-dialog-copy');
    try {
      await navigator.clipboard.writeText(detailClipboardText);
      button.textContent = 'Copiado';
    } catch {
      button.textContent = 'Falha ao copiar';
    }
    setTimeout(() => { button.textContent = 'Copiar detalhes'; }, 1500);
  });

  async function refreshAll() {
    const filters = currentFilters(filterForm);
    if (filters.from && filters.to && filters.from > filters.to) {
      refreshStatus.textContent = 'A data inicial deve vir antes da final';
      return;
    }

    dashboardMain.classList.add('loading');
    dashboardMain.setAttribute('aria-busy', 'true');
    refreshStatus.textContent = 'Atualizando fontes…';
    updatePeriodLabel(filters);

    const names = [...new Set([...SUMMARY_STATS, ...CHARTS.map((chart) => chart.name)])];
    const [statEntries, bugs, updaterEvents] = await Promise.all([
      Promise.all(names.map(async (name) => [name, await fetchStat(name, filters)])),
      requestJson(buildBugsUrl(API_BASE, filters)),
      requestJson(buildUpdaterEventsUrl(API_BASE, filters)),
    ]);
    const results = Object.fromEntries(statEntries);

    if (Object.values(results).some((result) => result.unauthorized) || bugs.unauthorized || updaterEvents.unauthorized) {
      dashboardMain.classList.remove('loading');
      dashboardMain.removeAttribute('aria-busy');
      showLogin('Sua sessão expirou.');
      return;
    }

    renderSummary(results, bugs);
    renderReliabilityTable(results['reliability-by-version']?.data);
    feeds.set('recentFailures', results['recent-failures']?.data, Boolean(results['recent-failures']?.error));
    feeds.set('bugReports', bugs.data, Boolean(bugs.error));
    feeds.set('updaterEvents', updaterEvents.data, Boolean(updaterEvents.error));
    byId('csv-bug-reports').href = buildBugsCsvUrl(API_BASE, filters);
    byId('csv-recent-failures').href = buildCsvUrl(API_BASE, 'recent-failures', filters);

    CHARTS.forEach((definition) => renderChart(definition, results[definition.name], filters));
    renderHealthSignals(results, bugs);

    const failures = [...Object.values(results), bugs, updaterEvents].filter((result) => result.error).length;
    refreshStatus.textContent = failures ? `Atualizado com ${failures} fonte(s) indisponível(is)` : `Atualizado às ${new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`;
    dashboardMain.classList.remove('loading');
    dashboardMain.removeAttribute('aria-busy');
  }

  const probe = await fetchStat('success-rate', {});
  if (probe.unauthorized) {
    showLogin();
  } else if (probe.error) {
    showLogin('Não foi possível conectar ao Worker administrativo.');
  } else {
    const csrf = await getCsrfToken(API_BASE);
    if (csrf.unauthorized) return showLogin();
    if (csrf.error || typeof csrf.data?.csrfToken !== 'string') return showLogin('Não foi possível validar a sessão.');
    csrfToken = csrf.data.csrfToken;
    showDashboard();
    await Promise.all([refreshAll(), loadLiveAlert()]);
  }

  function fetchStat(name, filters) {
    return requestJson(buildStatsUrl(API_BASE, name, filters));
  }
}

function renderSummary(results, bugReportsResult) {
  const successRow = results['success-rate']?.data?.[0];
  const account = results['account-summary']?.data?.[0] ?? {};
  const ai = results['ai-summary']?.data?.[0] ?? {};
  const billing = results['billing-payments']?.data?.[0] ?? {};
  const updater = results['updater-summary']?.data?.[0] ?? {};
  const subscriptions = results['billing-subscriptions']?.data ?? [];
  const errors = results['error-categories']?.data ?? [];
  const runs = results['runs-per-day']?.data ?? [];
  const aiCompleted = Number(ai.completed) || 0;
  const aiRequests = Number(ai.requests) || 0;

  setText('tile-total-runs', metricText(results['runs-per-day'], number.format(sumBy(runs, 'runs'))));
  setText('tile-success-rate', metricText(results['success-rate'], formatPercent(computeSuccessRatePercent(successRow))));
  setText('tile-total-accounts', metricText(results['account-summary'], number.format(Number(account.total_accounts) || 0)));
  setText('tile-new-accounts', metricText(results['account-summary'], `${number.format(Number(account.new_accounts) || 0)} novas no período`));
  setText('tile-ai-active', metricText(results['ai-summary'], number.format(Number(ai.active_accounts) || 0)));
  setText('tile-active-subscriptions', metricText(results['billing-subscriptions'], number.format(sumRows(subscriptions.filter((row) => row.state === 'authorized'), 'subscriptions'))));
  setText('tile-net-revenue', metricText(results['billing-payments'], brl.format((Number(billing.net_revenue_cents) || 0) / 100)));
  setText('metric-ai-requests', metricText(results['ai-summary'], number.format(aiRequests)));
  setText('metric-ai-success', metricText(results['ai-summary'], aiRequests ? formatPercent((aiCompleted / aiRequests) * 100) : '—'));
  setText('metric-ai-cost', metricText(results['ai-summary'], usd.format((Number(ai.cost_microusd) || 0) / 1_000_000)));
  setText('metric-payment-problems', metricText(results['billing-payments'], number.format(Number(billing.problem_payments) || 0)));
  setText('tile-total-failures', metricText(results['error-categories'], number.format(sumBy(errors, 'occurrences'))));
  setText('tile-average-time', metricText(results['average-time'], formatDuration(results['average-time']?.data?.[0]?.average_ms)));
  setText('metric-updater-failures', metricText(results['updater-summary'], number.format(Number(updater.failed) || 0)));
  setText('metric-bug-reports', metricText(bugReportsResult, number.format((bugReportsResult.data ?? []).length)));
}

function renderHealthSignals(results, bugReportsResult) {
  const success = computeSuccessRatePercent(results['success-rate']?.data?.[0]);
  const updaterFailed = Number(results['updater-summary']?.data?.[0]?.failed) || 0;
  const paymentProblems = Number(results['billing-payments']?.data?.[0]?.problem_payments) || 0;
  const runs = results['runs-per-day']?.data ?? [];
  const initializations = sumBy(results['app-initializations-per-day']?.data ?? [], 'initializations');
  const abandoned = sumBy(results['abandoned-optimizations']?.data ?? [], 'abandoned');
  const benchmarkFailures = sumBy(
    (results['gtav-benchmark-outcomes']?.data ?? []).filter((row) => row.event_name === 'gtav-benchmark-failed'),
    'runs');
  const signals = [];

  if (!results['success-rate']?.error) {
    if (success === null) signals.push(['warning', 'Sem otimizações suficientes para calcular confiabilidade no período.']);
    else if (success < 95) signals.push(['danger', `Taxa de sucesso em ${formatPercent(success)}; priorize a análise por versão.`]);
    else if (success < 98) signals.push(['warning', `Taxa de sucesso em ${formatPercent(success)}; acompanhe os códigos recorrentes.`]);
    else signals.push(['', `Confiabilidade das otimizações em ${formatPercent(success)}.`]);
  }

  if (!results['app-initializations-per-day']?.error && initializations) {
    signals.push(['', `Sinais de inicialização saudável recebidos: ${number.format(initializations)}.`]);
  }
  if (!results['abandoned-optimizations']?.error && abandoned) {
    signals.push(['warning', `${number.format(abandoned)} otimização(ões) iniciada(s) sem resultado terminal.`]);
  }
  if (!results['gtav-benchmark-outcomes']?.error && benchmarkFailures) {
    signals.push(['warning', `${number.format(benchmarkFailures)} benchmark(s) do GTAV falharam no período.`]);
  }

  if (!results['updater-summary']?.error) signals.push(updaterFailed
    ? ['danger', `${number.format(updaterFailed)} falha(s) de atualização no período.`]
    : ['', 'Nenhuma falha de atualização registrada no período.']);
  if (!results['billing-payments']?.error) signals.push(paymentProblems
    ? ['warning', `${number.format(paymentProblems)} pagamento(s) rejeitado(s) ou contestado(s).`]
    : ['', 'Nenhum pagamento problemático registrado no período.']);

  const bugReports = bugReportsResult.data ?? [];
  if (!bugReportsResult.error && bugReports.length) signals.push(['warning', `${number.format(bugReports.length)} relato(s) no feed do recorte atual.`]);
  if (!results['runs-per-day']?.error && !runs.length) signals.push(['warning', 'Nenhuma telemetria anônima recebida para os filtros atuais.']);
  if (!signals.length) signals.push(['warning', 'Sinais de saúde indisponíveis; verifique a conexão com as fontes administrativas.']);

  const container = byId('health-signals');
  container.replaceChildren(...signals.slice(0, 6).map(([severity, message]) => {
    const item = document.createElement('div');
    item.className = `health-item ${severity}`.trim();
    item.textContent = message;
    return item;
  }));
}

function renderReliabilityTable(rows = []) {
  const body = byId('reliability-versions-body');
  body.replaceChildren();
  if (!rows.length) return body.append(emptyRow(6, 'Sem versões no período'));
  rows.forEach((row) => {
    const total = Number(row.total) || 0;
    const completed = Number(row.completed) || 0;
    const values = [
      formatAppVersion(row.app_version),
      total ? formatPercent((completed / total) * 100) : '—',
      number.format(completed),
      number.format(Number(row.failed) || 0),
      number.format(Number(row.cancelled) || 0),
      number.format(total),
    ];
    const tr = document.createElement('tr');
    values.forEach((value, index) => {
      const td = document.createElement('td');
      td.textContent = value;
      if (index === 1 && total && completed / total < .95) td.className = 'failure-code';
      tr.appendChild(td);
    });
    body.appendChild(tr);
  });
}

function renderChart(definition, result, filters) {
  const canvas = byId(`chart-${definition.name}`);
  if (!canvas) return;
  const link = byId(`csv-${definition.name}`);
  if (link) link.href = buildCsvUrl(API_BASE, definition.name, filters);
  const rows = result?.error || result?.unauthorized ? [] : result?.data ?? [];
  const options = { horizontal: definition.horizontal, color: definition.color, valueLabel: definition.label };

  if (definition.type === 'line') {
    drawLineChart(canvas, toLineSeries(rows, definition.x, definition.y), options);
    return;
  }

  let series;
  if (definition.keys) {
    series = toCombinedBarSeries(rows.map((row) => ({ ...row, app_version: formatAppVersion(row.app_version) })), definition.keys, definition.value);
  } else {
    series = toBarSeries(rows, definition.key, definition.value);
  }
  series = topN(series, definition.limit ?? 5);
  if (definition.format) series = series.map((point) => ({ ...point, label: definition.format(point.label) }));

  if (definition.type === 'donut') {
    drawDonutChart(canvas, series, options);
    renderLegend(definition.legend, series);
  } else {
    drawBarChart(canvas, series, options);
  }
}

function createFeeds(detailDialog, setClipboard) {
  const definitions = {
    recentFailures: {
      body: byId('recent-failures-body'), button: byId('recent-failures-toggle'), label: 'falhas recentes', colspan: 7,
      map: toRecentFailureRow, detail: openFailureDetails, classes: (index) => index === 1 ? 'failure-code' : '',
    },
    bugReports: {
      body: byId('bug-reports-body'), button: byId('bug-reports-toggle'), label: 'bugs reportados', colspan: 6,
      map: toBugReportRow, detail: openBugReportDetails, classes: (index) => index === 1 ? 'failure-code' : index === 2 ? 'summary-cell' : '',
    },
    updaterEvents: {
      body: byId('updater-events-body'), button: byId('updater-events-toggle'), label: 'eventos do updater', colspan: 8,
      map: toUpdaterEventRow,
    },
  };
  const state = Object.fromEntries(Object.keys(definitions).map((key) => [key, { rows: [], expanded: false, error: false }]));

  Object.entries(definitions).forEach(([name, definition]) => definition.button.addEventListener('click', () => {
    state[name].expanded = !state[name].expanded;
    render(name);
  }));

  function render(name) {
    const definition = definitions[name];
    let rows = state[name].rows;
    if (name === 'recentFailures') {
      const query = byId('incident-search').value.trim().toLocaleLowerCase('pt-BR');
      if (query) rows = rows.filter((row) => Object.values(row).some((value) => String(value ?? '').toLocaleLowerCase('pt-BR').includes(query)));
    }
    const shown = limitFeedRows(rows, state[name].expanded);
    definition.body.replaceChildren();
    if (!shown.length) {
      definition.body.append(emptyRow(definition.colspan, state[name].error ? 'Fonte indisponível; tente atualizar novamente' : 'Nenhum resultado para os filtros atuais'));
    } else {
      shown.forEach((source, rowIndex) => {
        const tr = document.createElement('tr');
        definition.map(source).forEach((value, cellIndex) => {
          const td = document.createElement('td');
          td.textContent = value;
          if (definition.classes) td.className = definition.classes(cellIndex);
          tr.appendChild(td);
        });
        if (definition.detail) {
          const action = document.createElement('td');
          const button = document.createElement('button');
          button.type = 'button';
          button.className = 'details-button';
          button.textContent = 'Ver';
          button.setAttribute('aria-label', `Ver detalhes da linha ${rowIndex + 1}`);
          button.addEventListener('click', () => definition.detail(source));
          action.appendChild(button);
          tr.appendChild(action);
        }
        definition.body.appendChild(tr);
      });
    }
    definition.button.hidden = rows.length <= 5;
    definition.button.textContent = state[name].expanded ? '−' : '+';
    definition.button.setAttribute('aria-expanded', String(state[name].expanded));
    definition.button.setAttribute('aria-label', `${state[name].expanded ? 'Resumir' : 'Expandir'} ${definition.label}`);
  }

  function set(name, rows, error = false) {
    state[name].rows = rows ?? [];
    state[name].error = error;
    render(name);
  }

  function openFailureDetails(row) {
    const actions = formatActionIds(row.action_ids);
    showDetails({
      kind: 'Detalhes do erro',
      title: row.bug_code || 'Falha sem código específico',
      meta: [['Ocorrência', display(row.event_id)], ['Recebido em', toRecentFailureRow(row)[0]], ['Versão', formatAppVersion(row.app_version)], ['Ambiente', display(row.environment)], ['Perfil', display(row.profile)]],
      sections: [
        ['Código da falha', display(row.bug_code)],
        ['Categoria', display(row.error_category)],
        ['Tempo de execução', formatDuration(row.execution_time_ms)],
        ['Contexto técnico', `Windows: ${display(row.os_version)}\nArquitetura: ${display(row.system_architecture)}\nCPU: ${display(row.cpu_model)}\nGPU: ${display(row.gpu_model)}\nAções no plano: ${display(row.optimization_target_count)}`],
        ['IDs das ações no plano', actions.length ? actions : ['Não informado pela telemetria opcional.']],
      ],
    });
  }

  function openBugReportDetails(row) {
    showDetails({
      kind: 'Detalhes do relato',
      title: row.bug_code || 'Relato sem código específico',
      meta: [['Ocorrência', display(row.report_id)], ['Recebido em', toBugReportRow(row)[0]], ['Versão', formatAppVersion(row.app_version)], ['Ambiente', display(row.environment)], ['Perfil', display(row.profile)]],
      sections: [['Resumo', display(row.summary)], ['Descrição', display(row.description)], ['Contexto técnico', display(row.technical_summary)], ['E-mail', display(row.email)], ['Trecho de log', row.log_text || 'Nenhum trecho de log enviado.', 'code']],
    });
  }

  function showDetails({ kind, title, meta, sections }) {
    byId('detail-dialog-kind').textContent = kind;
    byId('detail-dialog-title').textContent = title;
    byId('detail-dialog-meta').replaceChildren(...meta.map(([label, value]) => {
      const wrapper = document.createElement('div');
      const dt = document.createElement('dt');
      const dd = document.createElement('dd');
      dt.textContent = label;
      dd.textContent = value;
      dd.title = value;
      wrapper.append(dt, dd);
      return wrapper;
    }));
    byId('detail-dialog-content').replaceChildren(...sections.map(([heading, value, style]) => {
      const section = document.createElement('section');
      section.className = 'detail-section';
      const h3 = document.createElement('h3');
      h3.textContent = heading;
      const content = Array.isArray(value) ? document.createElement('ul') : document.createElement(style === 'code' ? 'pre' : 'p');
      if (Array.isArray(value)) content.append(...value.map((item) => {
        const li = document.createElement('li');
        li.textContent = item;
        return li;
      }));
      else content.textContent = value;
      section.append(h3, content);
      return section;
    }));
    setClipboard([`${kind}: ${title}`, ...meta.map(([label, value]) => `${label}: ${value}`), ...sections.map(([heading, value]) => `${heading}:\n${Array.isArray(value) ? value.join('\n') : value}`)].join('\n\n'));
    detailDialog.showModal();
  }

  return { set, render };
}

function metricText(result, value) {
  return result?.error ? '—' : value;
}

function renderLegend(id, series) {
  const container = byId(id);
  if (!container) return;
  container.replaceChildren(...toDistributionRows(series).map((point, index) => {
    const row = document.createElement('div');
    row.className = 'legend-row';
    const swatch = document.createElement('span');
    swatch.className = 'legend-swatch';
    swatch.style.backgroundColor = DONUT_COLORS[index % DONUT_COLORS.length];
    const label = document.createElement('span');
    label.className = 'legend-name';
    label.textContent = point.label;
    const value = document.createElement('span');
    value.className = 'legend-value';
    value.textContent = `${number.format(point.value)} · ${Math.round(point.percent * 10) / 10}%`;
    row.append(swatch, label, value);
    return row;
  }));
}

function currentFilters(form) {
  const data = new FormData(form);
  return {
    from: data.get('from') || undefined,
    to: data.get('to') || undefined,
    version: data.get('version') || undefined,
    environment: data.get('environment') || undefined,
  };
}

function setPeriod(days) {
  const today = new Date();
  const from = new Date(today);
  from.setUTCDate(from.getUTCDate() - days + 1);
  const form = byId('filter-form');
  form.elements.from.value = from.toISOString().slice(0, 10);
  form.elements.to.value = today.toISOString().slice(0, 10);
}

function updatePeriodLabel(filters) {
  const environment = { Production: 'Produção', Development: 'Desenvolvimento', All: 'Todos os ambientes' }[filters.environment] || 'Produção';
  const range = filters.from && filters.to ? `${formatDate(filters.from)} a ${formatDate(filters.to)}` : 'Todo o histórico';
  byId('period-label').textContent = `${range} · ${environment}${filters.version ? ` · v${filters.version}` : ''}`;
}

function wireNavigation() {
  const links = [...document.querySelectorAll('.side-nav a')];
  const sections = links.map((link) => byId(link.hash.slice(1)));
  if (typeof IntersectionObserver !== 'function') return;
  const observer = new IntersectionObserver((entries) => {
    const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
    if (!visible) return;
    links.forEach((link) => link.classList.toggle('active', link.hash === `#${visible.target.id}`));
  }, { rootMargin: '-18% 0px -70% 0px', threshold: [0, .2] });
  sections.forEach((section) => observer.observe(section));
}

function formatOutcome(value) {
  return {
    'optimization-completed': 'Concluída',
    'optimization-failed': 'Falhou',
    'optimization-cancelled': 'Cancelada',
  }[value] || value;
}

function formatDate(value) {
  const [year, month, day] = value.split('-');
  return year && month && day ? `${day}/${month}/${year}` : value;
}

function display(value) {
  return value === null || value === undefined || value === '' ? '—' : String(value);
}

function sumRows(rows, key) {
  return rows.reduce((total, row) => total + (Number(row[key]) || 0), 0);
}

function emptyRow(colspan, message) {
  const tr = document.createElement('tr');
  const td = document.createElement('td');
  td.colSpan = colspan;
  td.className = 'empty-row';
  td.textContent = message;
  tr.appendChild(td);
  return tr;
}

function byId(id) {
  return document.getElementById(id);
}

function setText(id, value) {
  byId(id).textContent = value;
}

main().catch((error) => {
  console.error('Dashboard initialization failed:', error);
  const loginError = byId('login-error');
  if (loginError) loginError.textContent = 'Erro ao iniciar o dashboard. Recarregue a página.';
});
