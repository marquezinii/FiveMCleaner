import test from 'node:test';
import assert from 'node:assert/strict';
import { recentUpdaterEvents } from '../../src/updaterEvents/queries.js';

test('updater event query uses bound filters and limit', () => {
  const query = recentUpdaterEvents({ environment: 'Production', version: '1.2.0' }, 1000);
  assert.match(query.sql, /environment = \?/);
  assert.deepEqual(query.params, ['Production', '1.2.0', 500]);
});

test('updater event query treats the dashboard default environment "All" as no filter', () => {
  const query = recentUpdaterEvents({ environment: 'All', version: '1.2.0' }, 100);
  assert.doesNotMatch(query.sql, /environment = \?/);
  assert.deepEqual(query.params, ['1.2.0', 100]);
});

test('updater event query with no environment at all still filters by version and limit', () => {
  const query = recentUpdaterEvents({ version: '1.2.0' }, 100);
  assert.doesNotMatch(query.sql, /environment = \?/);
  assert.deepEqual(query.params, ['1.2.0', 100]);
});
