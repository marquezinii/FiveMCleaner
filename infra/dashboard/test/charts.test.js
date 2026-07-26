import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  toBarSeries,
  toCombinedBarSeries,
  toLineSeries,
  topN,
  computeSuccessRatePercent,
  formatDuration,
  formatPercent,
  sumBy,
  formatTimestamp,
  toRecentFailureRow,
  truncate,
  toBugReportRow,
} from '../assets/charts.js';

test('toBarSeries maps arbitrary label/value keys into a uniform shape', () => {
  const series = toBarSeries([{ os_version: 'Windows 11', runs: 78 }], 'os_version', 'runs');

  assert.deepEqual(series, [{ label: 'Windows 11', value: 78 }]);
});

test('toBarSeries coerces a missing or non-numeric value to zero instead of NaN', () => {
  const series = toBarSeries([{ label: 'x' }], 'label', 'value');

  assert.equal(series[0].value, 0);
});

test('toBarSeries returns an empty array for null/undefined input', () => {
  assert.deepEqual(toBarSeries(null, 'a', 'b'), []);
  assert.deepEqual(toBarSeries(undefined, 'a', 'b'), []);
});

test('toLineSeries maps and sorts by x ascending', () => {
  const series = toLineSeries(
    [
      { day: '2026-01-03', runs: 3 },
      { day: '2026-01-01', runs: 1 },
      { day: '2026-01-02', runs: 2 },
    ],
    'day',
    'runs',
  );

  assert.deepEqual(series, [
    { x: '2026-01-01', y: 1 },
    { x: '2026-01-02', y: 2 },
    { x: '2026-01-03', y: 3 },
  ]);
});

test('topN keeps only the first N entries', () => {
  const series = [{ label: 'a', value: 3 }, { label: 'b', value: 2 }, { label: 'c', value: 1 }];

  assert.deepEqual(topN(series, 2), [{ label: 'a', value: 3 }, { label: 'b', value: 2 }]);
});

test('computeSuccessRatePercent divides completed by total as a percentage', () => {
  assert.equal(computeSuccessRatePercent({ completed: 934, total: 1000 }), 93.4);
});

test('computeSuccessRatePercent returns null when there is no data yet', () => {
  assert.equal(computeSuccessRatePercent(null), null);
  assert.equal(computeSuccessRatePercent({ completed: 0, total: 0 }), null);
});

test('formatDuration renders seconds-only durations without a minutes part', () => {
  assert.equal(formatDuration(42_000), '42s');
});

test('formatDuration renders minutes and seconds for longer durations', () => {
  assert.equal(formatDuration(72_000), '1m 12s');
});

test('formatDuration renders a dash for missing data', () => {
  assert.equal(formatDuration(null), '—');
  assert.equal(formatDuration(undefined), '—');
  assert.equal(formatDuration(Number.NaN), '—');
});

test('formatPercent rounds to one decimal place and appends a percent sign', () => {
  assert.equal(formatPercent(93.44), '93.4%');
  assert.equal(formatPercent(100), '100%');
});

test('formatPercent renders a dash for missing data', () => {
  assert.equal(formatPercent(null), '—');
});

test('sumBy adds up a numeric column across every row', () => {
  assert.equal(sumBy([{ runs: 5 }, { runs: 7 }, { runs: 3 }], 'runs'), 15);
});

test('sumBy returns zero for an empty or missing row set', () => {
  assert.equal(sumBy([], 'runs'), 0);
  assert.equal(sumBy(undefined, 'runs'), 0);
});

test('toCombinedBarSeries joins two keys into one label', () => {
  const series = toCombinedBarSeries(
    [{ app_version: '1.0.4', error_category: 'timeout', occurrences: 5 }],
    ['app_version', 'error_category'],
    'occurrences',
  );

  assert.deepEqual(series, [{ label: '1.0.4 · timeout', value: 5 }]);
});

test('toCombinedBarSeries accepts a custom separator', () => {
  const series = toCombinedBarSeries(
    [{ a: 'x', b: 'y', value: 1 }],
    ['a', 'b'],
    'value',
    ' / ',
  );

  assert.deepEqual(series, [{ label: 'x / y', value: 1 }]);
});

test('formatTimestamp renders a compact, locale-independent date and time', () => {
  assert.equal(formatTimestamp('2026-07-25T22:30:05.123Z'), '2026-07-25 22:30');
});

test('formatTimestamp renders a dash for missing or malformed values', () => {
  assert.equal(formatTimestamp(null), '—');
  assert.equal(formatTimestamp(undefined), '—');
  assert.equal(formatTimestamp('not-a-date'), '—');
});

test('toRecentFailureRow maps a row into the table\'s exact column order', () => {
  const row = {
    received_at: '2026-07-25T22:30:05.000Z',
    error_category: 'timeout',
    app_version: '1.0.4',
    environment: 'Production',
    os_version: 'Windows 11',
    cpu_model: 'AMD Ryzen 5 5600X',
    gpu_model: 'NVIDIA GeForce RTX 5070',
    profile: 'Balanced',
  };

  assert.deepEqual(toRecentFailureRow(row), [
    '2026-07-25 22:30',
    'timeout',
    '1.0.4',
    'Production',
    'Windows 11',
    'AMD Ryzen 5 5600X',
    'NVIDIA GeForce RTX 5070',
    'Balanced',
  ]);
});

test('toRecentFailureRow substitutes a placeholder for missing optional fields', () => {
  const row = {
    received_at: '2026-07-25T22:30:05.000Z',
    error_category: 'timeout',
    app_version: '1.0.4',
    environment: 'Production',
    os_version: null,
    cpu_model: null,
    gpu_model: null,
    profile: null,
  };

  const cells = toRecentFailureRow(row);
  assert.equal(cells[4], '—');
  assert.equal(cells[5], '—');
  assert.equal(cells[6], '—');
  assert.equal(cells[7], '—');
});

test('truncate returns short text unchanged', () => {
  assert.equal(truncate('short text', 60), 'short text');
});

test('truncate cuts long text and appends an ellipsis', () => {
  const result = truncate('x'.repeat(100), 10);

  assert.equal(result.length, 10);
  assert.ok(result.endsWith('…'));
});

test('truncate renders a dash for empty or missing text', () => {
  assert.equal(truncate('', 10), '—');
  assert.equal(truncate(null, 10), '—');
  assert.equal(truncate(undefined, 10), '—');
});

test('toBugReportRow maps a row into the bug report table\'s column order', () => {
  const row = {
    received_at: '2026-07-26T10:00:00.000Z',
    category: 'Falha na otimização',
    summary: 'O preset não terminou',
    app_version: '1.0.4',
    profile: 'Médio',
    environment: 'Production',
    attachment_key: '11111111-1111-1111-1111-111111111111/captura-x.png',
  };

  assert.deepEqual(toBugReportRow(row), [
    '2026-07-26 10:00',
    'Falha na otimização',
    'O preset não terminou',
    '1.0.4',
    'Médio',
    'Production',
    'sim',
  ]);
});

test('toBugReportRow shows "não" when there is no attachment', () => {
  const row = {
    received_at: '2026-07-26T10:00:00.000Z',
    category: 'x',
    summary: 'x',
    app_version: '1.0.4',
    profile: 'Médio',
    environment: 'Production',
    attachment_key: null,
  };

  assert.equal(toBugReportRow(row).at(-1), 'não');
});
