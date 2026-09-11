import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  optimizationRunsPerDay,
  osVersionBreakdown,
  appVersionBreakdown,
  averageOptimizationTimeMs,
  successRate,
  appInitializationsPerDay,
  abandonedOptimizationFlows,
  gtaVBenchmarkOutcomes,
  errorsByVersion,
  errorCategoryBreakdown,
  bugCodeBreakdown,
  recentFailures,
  topCpuModels,
  topGpuModels,
  ramBucketBreakdown,
  gtaEditionBreakdown,
  fiveMInstallDetectionRate,
  diskTypeBreakdown,
  averageOptimizationTargetCount,
  backupStats,
  elevationUsageRate,
  windowsBuildBreakdown,
  optimizationOutcomeBreakdown,
  profileBreakdown,
  actionUsage,
  reliabilityByVersion,
  accountSummary,
  accountsPerDay,
  aiUsageSummary,
  aiUsagePerDay,
  billingSubscriptionBreakdown,
  billingPaymentSummary,
  updaterSummary,
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
  assert.match(sql, /received_at < date\(\?, '\+1 day'\)/);
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

test('averageOptimizationTimeMs only counts completed runs', () => {
  const { sql } = averageOptimizationTimeMs();

  assert.match(sql, /event_name = 'optimization-completed'/);
  assert.match(sql, /AVG\(execution_time_ms\)/);
});

test('successRate counts completed runs against the total', () => {
  const { sql } = successRate();

  assert.match(sql, /SUM\(CASE WHEN event_name = 'optimization-completed'/);
  assert.match(sql, /COUNT\(\*\) AS total/);
  assert.match(sql, /optimization-cancelled/);
});

test('expanded health and adoption queries stay scoped to their closed event families', () => {
  const startup = appInitializationsPerDay();
  const abandoned = abandonedOptimizationFlows();
  const benchmark = gtaVBenchmarkOutcomes();

  assert.match(startup.sql, /event_name = 'app-initialized'/);
  assert.match(abandoned.sql, /operation_id IS NOT NULL/);
  assert.match(abandoned.sql, /NOT EXISTS/);
  assert.match(benchmark.sql, /gtav-benchmark-completed/);
  assert.match(benchmark.sql, /AVG\(execution_time_ms\)/);
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

test('errorCategoryBreakdown counts failed runs grouped only by category, across every version', () => {
  const { sql } = errorCategoryBreakdown();

  assert.match(sql, /event_name = 'optimization-failed'/);
  assert.match(sql, /GROUP BY error_category/);
  assert.doesNotMatch(sql, /app_version/);
});

test('bugCodeBreakdown groups exact allowlisted codes for incident recognition', () => {
  const { sql } = bugCodeBreakdown();

  assert.match(sql, /event_name = 'optimization-failed'/);
  assert.match(sql, /bug_code IS NOT NULL/);
  assert.match(sql, /GROUP BY bug_code/);
});

test('recentFailures orders by received_at descending and applies a limit', () => {
  const { sql, params } = recentFailures({}, 15);

  assert.match(sql, /event_name = 'optimization-failed'/);
  assert.match(sql, /event_id/);
  assert.match(sql, /bug_code/);
  assert.match(sql, /GROUP_CONCAT\(action_id/);
  assert.match(sql, /ORDER BY received_at DESC/);
  assert.equal(params.at(-1), 15);
});

test('recentFailures defaults to a limit of 20', () => {
  const { params } = recentFailures();

  assert.equal(params.at(-1), 20);
});

test('environment "All" omits the environment filter entirely instead of matching a literal "All" row', () => {
  const { sql, params } = optimizationRunsPerDay({ environment: 'All' });

  assert.doesNotMatch(sql, /environment = \?/);
  assert.deepEqual(params, []);
});

test('environment "All" combined with other filters still applies those filters', () => {
  const { params } = optimizationRunsPerDay({ environment: 'All', appVersion: '1.0.4' });

  assert.deepEqual(params, ['1.0.4']);
});

test('an empty filter set (environment "All", nothing else) still produces valid, non-empty SQL', () => {
  const { sql } = optimizationRunsPerDay({ environment: 'All' });

  assert.match(sql, /WHERE 1=1/);
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
  ]) {
    const { params } = builder();
    assert.equal(params[0], 'Production');
  }
});

// --- v5: expanded diagnostic fields ---

test('gtaEditionBreakdown excludes nulls and groups by edition', () => {
  const { sql } = gtaEditionBreakdown();

  assert.match(sql, /gta_edition IS NOT NULL/);
  assert.match(sql, /GROUP BY gta_edition/);
});

test('fiveMInstallDetectionRate sums detected vs. total', () => {
  const { sql } = fiveMInstallDetectionRate();

  assert.match(sql, /SUM\(CASE WHEN five_m_install_detected = 1/);
  assert.match(sql, /five_m_install_detected IS NOT NULL/);
});

test('diskTypeBreakdown excludes nulls and orders by count descending', () => {
  const { sql } = diskTypeBreakdown();

  assert.match(sql, /disk_type IS NOT NULL/);
  assert.match(sql, /ORDER BY runs DESC/);
});

test('averageOptimizationTargetCount averages only non-null counts', () => {
  const { sql } = averageOptimizationTargetCount();

  assert.match(sql, /AVG\(optimization_target_count\)/);
  assert.match(sql, /optimization_target_count IS NOT NULL/);
});

test('backupStats sums created and restored independently', () => {
  const { sql } = backupStats();

  assert.match(sql, /SUM\(CASE WHEN backup_created = 1/);
  assert.match(sql, /SUM\(CASE WHEN backup_restored = 1/);
});

test('elevationUsageRate sums elevated vs. total', () => {
  const { sql } = elevationUsageRate();

  assert.match(sql, /SUM\(CASE WHEN elevation_used = 1/);
  assert.match(sql, /elevation_used IS NOT NULL/);
});

test('windowsBuildBreakdown excludes nulls and limits to the top 10', () => {
  const { sql } = windowsBuildBreakdown();

  assert.match(sql, /windows_build IS NOT NULL/);
  assert.match(sql, /LIMIT 10/);
});

test('product adoption queries remain aggregate and use bound telemetry filters', () => {
  const outcomes = optimizationOutcomeBreakdown({ appVersion: '1.6.1' });
  const profiles = profileBreakdown();
  const actions = actionUsage({ from: '2026-09-01' });

  assert.match(outcomes.sql, /GROUP BY event_name/);
  assert.deepEqual(outcomes.params, ['Production', '1.6.1']);
  assert.match(profiles.sql, /profile IS NOT NULL/);
  assert.match(actions.sql, /JOIN telemetry_events/);
  assert.doesNotMatch(actions.sql, /SELECT.*event_id/i);
  assert.deepEqual(actions.params, ['Production', '2026-09-01']);
});

test('reliability comparison keeps completed, failed and cancelled outcomes separate', () => {
  const { sql } = reliabilityByVersion();

  assert.match(sql, /AS completed/);
  assert.match(sql, /AS failed/);
  assert.match(sql, /AS cancelled/);
  assert.match(sql, /GROUP BY app_version/);
});

test('account and AI queries expose only aggregates and filter their own timestamps', () => {
  const accounts = accountSummary({ from: '2026-09-01', to: '2026-09-10' });
  const growth = accountsPerDay({ from: '2026-09-01' });
  const ai = aiUsageSummary({ to: '2026-09-10' });
  const aiDaily = aiUsagePerDay();

  assert.match(accounts.sql, /total_accounts/);
  assert.doesNotMatch(accounts.sql, /username|first_name|last_name|\buid\b/i);
  assert.deepEqual(accounts.params, ['2026-09-01', '2026-09-10']);
  assert.match(growth.sql, /created_at >= \?/);
  assert.match(ai.sql, /COUNT\(DISTINCT account_uid\) AS active_accounts/);
  assert.doesNotMatch(ai.sql, /SELECT\s+account_uid/i);
  assert.match(aiDaily.sql, /GROUP BY day/);
});

test('billing queries summarize state and money without provider or account identifiers', () => {
  const subscriptions = billingSubscriptionBreakdown();
  const payments = billingPaymentSummary({ from: '2026-09-01' });

  assert.match(subscriptions.sql, /GROUP BY state/);
  assert.match(payments.sql, /net_revenue_cents/);
  assert.match(payments.sql, /updated_at >= \?/);
  assert.doesNotMatch(payments.sql, /account_uid|provider_payment_id/);
});

test('updater dashboard queries honor environment, date and candidate version filters', () => {
  const filters = { environment: 'Production', from: '2026-09-01', to: '2026-09-10', appVersion: '1.6.1' };
  const summary = updaterSummary(filters);

  assert.deepEqual(summary.params, ['Production', '2026-09-01', '2026-09-10', '1.6.1']);
  assert.match(summary.sql, /outcome = 'failed'/);
  assert.match(summary.sql, /candidate_version/);
});
