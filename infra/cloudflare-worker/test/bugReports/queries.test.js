import { test } from 'node:test';
import assert from 'node:assert/strict';
import { recentBugReports } from '../../src/bugReports/queries.js';

test('recentBugReports with no filters has no WHERE clause and defaults the limit', () => {
  const { sql, params } = recentBugReports();

  assert.doesNotMatch(sql, /WHERE/);
  assert.equal(params.at(-1), 50);
});

test('recentBugReports filters by environment when not "All"', () => {
  const { sql, params } = recentBugReports({ environment: 'Production' });

  assert.match(sql, /environment = \?/);
  assert.equal(params[0], 'Production');
});

test('recentBugReports omits the environment filter when it is "All"', () => {
  const { sql, params } = recentBugReports({ environment: 'All' });

  assert.doesNotMatch(sql, /environment = \?/);
  assert.deepEqual(params, [50]);
});

test('recentBugReports applies category and date range filters together', () => {
  const { params } = recentBugReports({
    environment: 'Production',
    category: 'Falha na otimização',
    from: '2026-01-01',
    to: '2026-01-31',
  });

  assert.deepEqual(params, ['Production', 'Falha na otimização', '2026-01-01', '2026-01-31', 50]);
});

test('recentBugReports orders by received_at descending', () => {
  const { sql } = recentBugReports();

  assert.match(sql, /ORDER BY received_at DESC/);
});

test('recentBugReports clamps the limit to the maximum allowed', () => {
  const { params } = recentBugReports({}, 10_000);

  assert.equal(params.at(-1), 200);
});

test('recentBugReports falls back to the default limit for an invalid value', () => {
  const { params } = recentBugReports({}, Number.NaN);

  assert.equal(params.at(-1), 50);
});

test('recentBugReports selects email and log_text for the dashboard table', () => {
  const { sql } = recentBugReports();

  assert.match(sql, /\bemail\b/);
  assert.match(sql, /log_text/);
});
