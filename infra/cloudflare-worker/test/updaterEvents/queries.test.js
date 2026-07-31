import test from 'node:test';
import assert from 'node:assert/strict';
import { recentUpdaterEvents } from '../../src/updaterEvents/queries.js';

test('updater event query uses bound filters and limit', () => {
  const query = recentUpdaterEvents({ environment: 'Production', version: '1.2.0' }, 1000);
  assert.match(query.sql, /environment = \?/);
  assert.deepEqual(query.params, ['Production', '1.2.0', 500]);
});
