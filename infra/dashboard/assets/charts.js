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
