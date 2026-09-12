import { test } from 'node:test';
import assert from 'node:assert/strict';
import { ALLOWED_BUG_CODES } from '../src/bugCodes.js';

test('ALLOWED_BUG_CODES includes the newly added app inventory and security health codes', () => {
  assert.ok(ALLOWED_BUG_CODES.has('APP_INV_SCAN'));
  assert.ok(ALLOWED_BUG_CODES.has('SEC_HEALTH_QUERY'));
});
