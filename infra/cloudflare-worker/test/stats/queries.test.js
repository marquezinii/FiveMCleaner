import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  optimizationRunsPerDay,
  osVersionBreakdown,
  appVersionBreakdown,
  topActions,
  averageOptimizationTimeMs,
  successRate,
  errorsByVersion,
  topCpuModels,
  topGpuModels,
  ramBucketBreakdown,
  profileBreakdown,
} from '../../src/stats/queries.js';

test('optimizationRunsPerDay defaults to the Production environment', () => {
  const { sql, params } = optimizationRunsPerDay();

  assert.match(sql, /GROUP BY day/);
  assert.deepEqual(params, ['Production']);
});

test('optimizationRunsPerDay applies date range and version filters as bound parameters, never string interpolation', () => {
  const { sql, params } = optimizationRunsPerDay({ from: '2026-01-01', to: '2026-01-31', appVersion: '1.0.4' });

  assert.doesNotMatch(sql, /2026-01-01/);
  assert.doesNotMatch(sql, /1\.0\.4/);
  assert.deepEqual(params, ['Production', '2026-01-01', '2026-01-31', '1.0.4']);
});

test('optimizationRunsPerDay honors an explicit environment override', () => {
  const { params } = optimizationRunsPerDay({ environment: 'Development' });

  assert.deepEqual(params, ['Development']);
});

test('osVersionBreakdown excludes null os_version and orders by count descending', () => {
  const { sql } = osVersionBreakdown();

  assert.match(sql, /os_version IS NOT NULL/);
  assert.match(sql, /ORDER BY runs DESC/);
});

test('appVersionBreakdown groups by app_version', () => {
  const { sql } = appVersionBreakdown();

  assert.match(sql, /GROUP BY app_version/);
});

test('topActions joins telemetry_event_actions with telemetry_events and applies the limit as a bound parameter', () => {
  const { sql, params } = topActions({}, 5);

  assert.match(sql, /JOIN telemetry_events/);
  assert.match(sql, /LIMIT \?/);
  assert.equal(params.at(-1), 5);
});

test('topActions defaults to a top-10 limit', () => {
  const { params } = topActions();

  assert.equal(params.at(-1), 10);
});

test('averageOptimizationTimeMs only counts completed runs', () => {
  const { sql } = averageOptimizationTimeMs();

  assert.match(sql, /event_name = 'optimization-completed'/);
  assert.match(sql, /AVG\(execution_time_ms\)/);
});

test('successRate counts completed runs against the total', () => {
  const { sql } = successRate();

  assert.match(sql, /SUM\(CASE WHEN event_name = 'optimization-completed'/);
  assert.match(sql, /COUNT\(\*\) AS total/);
});

test('errorsByVersion only counts failed runs with a known error category', () => {
  const { sql } = errorsByVersion();

  assert.match(sql, /event_name = 'optimization-failed'/);
  assert.match(sql, /error_category IS NOT NULL/);
  assert.match(sql, /GROUP BY app_version, error_category/);
});

test('topCpuModels and topGpuModels each exclude nulls and apply a limit', () => {
  const cpu = topCpuModels({}, 3);
  const gpu = topGpuModels({}, 3);

  assert.match(cpu.sql, /cpu_model IS NOT NULL/);
  assert.equal(cpu.params.at(-1), 3);
  assert.match(gpu.sql, /gpu_model IS NOT NULL/);
  assert.equal(gpu.params.at(-1), 3);
});

test('ramBucketBreakdown orders numerically by bucket size', () => {
  const { sql } = ramBucketBreakdown();

  assert.match(sql, /ram_bucket_gib IS NOT NULL/);
  assert.match(sql, /ORDER BY ram_bucket_gib ASC/);
});

test('profileBreakdown excludes null profiles', () => {
  const { sql } = profileBreakdown();

  assert.match(sql, /profile IS NOT NULL/);
});

test('every query filters by environment as the first bound parameter, never omitted', () => {
  for (const builder of [
    optimizationRunsPerDay,
    osVersionBreakdown,
    appVersionBreakdown,
    averageOptimizationTimeMs,
    successRate,
    errorsByVersion,
    ramBucketBreakdown,
    profileBreakdown,
  ]) {
    const { params } = builder();
    assert.equal(params[0], 'Production');
  }
});
