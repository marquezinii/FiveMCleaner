import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  toBarSeries,
  toLineSeries,
  topN,
  computeSuccessRatePercent,
  formatDuration,
  formatPercent,
  sumBy,
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
