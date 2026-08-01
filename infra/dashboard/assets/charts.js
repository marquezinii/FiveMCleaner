// Pure data-shaping for every dashboard chart/stat tile. Kept separate from
// canvas/DOM rendering (rendering.js) so this logic is unit testable
// without a browser -- see chart-data-shaping tests under test/.

/** Turns `{label, value}`-shaped rows (any two keys) into a bar-chart series. */
export function toBarSeries(rows, labelKey, valueKey) {
  return (rows ?? []).map((row) => ({
    label: String(row[labelKey]),
    value: Number(row[valueKey]) || 0,
  }));
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
 * locale-independent "YYYY-MM-DD HH:mm" for the recent-failures table --
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

  return isoString.slice(0, 16).replace('T', ' ');
}

/**
 * Maps one `recentFailures` row into the exact column order the table
 * renders, substituting a placeholder for any missing optional field
 * instead of showing a blank cell.
 */
export function toRecentFailureRow(row) {
  const fallback = (value) => value ?? '—';
  return [
    formatTimestamp(row.received_at),
    fallback(row.error_category),
    fallback(row.app_version),
    fallback(row.environment),
    fallback(row.os_version),
    fallback(row.cpu_model),
    fallback(row.gpu_model),
    fallback(row.profile),
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
  const fallback = (value) => value ?? '—';
  return [
    formatTimestamp(row.received_at),
    fallback(row.category),
    truncate(row.summary, 60),
    fallback(row.app_version),
    fallback(row.profile),
    fallback(row.environment),
    fallback(row.email),
    row.log_text ? 'sim' : 'não',
  ];
}

export function toUpdaterEventRow(row) {
  const fallback = (value) => value ?? '—';
  return [
    formatTimestamp(row.received_at),
    fallback(row.stage),
    fallback(row.outcome),
    fallback(row.error_code),
    fallback(row.previous_version),
    fallback(row.candidate_version),
    fallback(row.environment),
  ];
}
