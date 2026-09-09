// Pure data-shaping for every dashboard chart/stat tile. Kept separate from
// canvas/DOM rendering (rendering.js) so this logic is unit testable
// without a browser -- see chart-data-shaping tests under test/.

/** Substitutes a placeholder for any missing optional field instead of a blank cell. */
const fallback = (value) => value ?? '—';

// Mirrors Ralven.Contracts.BugCodeCatalog's category prefixes (the part of
// the code before the first underscore). Kept as a small hand-maintained
// map, same convention already used for bug_code itself in this dashboard —
// there is no shared package between the Worker/dashboard and the .NET app.
const BUG_CODE_CATEGORY_LABELS = {
  APP: 'Aplicativo',
  UPD: 'Atualização',
  BRK: 'Privilégios administrativos',
  NET: 'Rede',
  FIVEM: 'FiveM',
  GTAV: 'GTA V',
  WIN: 'Windows',
  CFG: 'Configuração',
  SYS: 'Sistema',
  SEC: 'Segurança',
};

/**
 * Formats a bug code with its category label, e.g. 'APP_OPT_ACTION_EXECUTION — Aplicativo'.
 * If the code's prefix is not recognized, returns the code unchanged.
 * If there is no code, returns the placeholder.
 */
function bugCodeWithCategory(code) {
  if (!code) return fallback(code);
  const category = BUG_CODE_CATEGORY_LABELS[code.split('_')[0]];
  return category ? `${code} — ${category}` : code;
}

/** Turns `{label, value}`-shaped rows (any two keys) into a bar-chart series. */
export function toBarSeries(rows, labelKey, valueKey) {
  return (rows ?? []).map((row) => ({
    label: String(row[labelKey]),
    value: Number(row[valueKey]) || 0,
  }));
}

/** Extracts the stable SemVer from legacy labels without exposing executable names or hashes. */
export function formatAppVersion(value) {
  const match = String(value ?? '').match(/(?:^|[^\d])(\d+\.\d+\.\d+)(?:$|[^\d])/);
  return match ? match[1] : 'Versão desconhecida';
}

/** Produces percentage rows for a compact chart legend from displayed data. */
export function toDistributionRows(series) {
  const items = topN(series, 5);
  const total = items.reduce((sum, item) => sum + item.value, 0);
  return items.map((item) => ({ ...item, percent: total ? (item.value / total) * 100 : 0 }));
}

/** Turns rows into an x/y line-chart series, sorted by x ascending. */
export function toLineSeries(rows, xKey, yKey) {
  return (rows ?? [])
    .map((row) => ({ x: row[xKey], y: Number(row[yKey]) || 0 }))
    .sort((a, b) => (a.x < b.x ? -1 : a.x > b.x ? 1 : 0));
}

/**
 * Same idea as {@link toBarSeries}, but joins two or more row keys into a
 * single label -- used for two-dimensional breakdowns (e.g. version +
 * error category) that still render fine as one bar chart once combined
 * into one label per bar.
 */
export function toCombinedBarSeries(rows, labelKeys, valueKey, separator = ' · ') {
  return (rows ?? []).map((row) => ({
    label: labelKeys.map((key) => String(row[key])).join(separator),
    value: Number(row[valueKey]) || 0,
  }));
}

/** Keeps only the top N entries of an already-sorted-descending series. */
export function topN(series, n) {
  return (series ?? []).slice(0, n);
}

/** Shows the five newest feed rows until its compact toggle is expanded. */
export function limitFeedRows(rows, expanded) {
  return expanded ? (rows ?? []) : (rows ?? []).slice(0, 5);
}

/**
 * `successRate` query returns `{completed, total}`; converts that into a
 * percentage, or `null` when there is no data yet (never divides by zero).
 */
export function computeSuccessRatePercent(row) {
  if (!row || !row.total) {
    return null;
  }

  return (row.completed / row.total) * 100;
}

/** Formats milliseconds as a short human duration, e.g. "42s" or "1m 12s". */
export function formatDuration(milliseconds) {
  if (milliseconds === null || milliseconds === undefined || Number.isNaN(milliseconds)) {
    return '—';
  }

  const totalSeconds = Math.round(milliseconds / 1000);
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return minutes > 0 ? `${minutes}m ${seconds}s` : `${seconds}s`;
}

/** Formats a fraction of 100 as e.g. "78%", or "—" when there is no data. */
export function formatPercent(value) {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return '—';
  }

  return `${Math.round(value * 10) / 10}%`;
}

/**
 * Sums every `runs`-shaped row's count -- used for stat tiles like
 * "Otimizações hoje" that need a single total instead of a per-day series.
 */
export function sumBy(rows, valueKey) {
  return (rows ?? []).reduce((total, row) => total + (Number(row[valueKey]) || 0), 0);
}

/**
 * Formats an ISO timestamp (as stored in `received_at`) as a compact,
 * locale-dependent local time for the recent-failures table --
 * never throws on a missing/malformed value.
 */
export function formatTimestamp(isoString) {
  if (!isoString) {
    return '—';
  }

  const date = new Date(isoString);
  if (Number.isNaN(date.getTime())) {
    return '—';
  }

  // Convert UTC to local timezone for display
  return date.toLocaleString(undefined, {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', hour12: false
  }).replace(',', '');
}

/**
 * Maps one `recentFailures` row into the exact column order the table
 * renders, substituting a placeholder for any missing optional field
 * instead of showing a blank cell.
 */
export function toRecentFailureRow(row) {
  return [
    formatTimestamp(row.received_at),
    fallback(row.bug_code),
    fallback(row.error_category),
    formatAppVersion(row.app_version),
    fallback(row.profile),
    fallback(row.environment),
  ];
}

/**
 * Truncates a long text field (e.g. a bug report summary) to a fixed length
 * for compact table display, appending an ellipsis when it was cut.
 */
export function truncate(text, maxLength) {
  if (!text) {
    return '—';
  }

  return text.length > maxLength ? `${text.slice(0, maxLength - 1)}…` : text;
}

/**
 * Maps one bug report row (from `/api/bugs`) into the exact column order
 * the "Bugs reportados" table renders. Text-only -- no attachment/screenshot
 * column, since that feature (and its R2 dependency) was dropped.
 */
export function toBugReportRow(row) {
  return [
    formatTimestamp(row.received_at),
    bugCodeWithCategory(row.bug_code),
    truncate(row.summary, 60),
    fallback(row.category),
    formatAppVersion(row.app_version),
  ];
}

export function formatActionIds(value) {
  if (!value) return [];
  return String(value).split(',').map((item) => item.trim()).filter(Boolean);
}

export function toUpdaterEventRow(row) {
  return [
    formatTimestamp(row.received_at),
    fallback(row.error_id ?? row.error_code?.toUpperCase()),
    fallback(row.error_name),
    fallback(row.stage),
    fallback(row.outcome),
    formatAppVersion(row.previous_version),
    formatAppVersion(row.candidate_version),
    fallback(row.environment),
  ];
}
