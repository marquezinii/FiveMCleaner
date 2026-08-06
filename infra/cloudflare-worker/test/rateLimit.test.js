import { test } from 'node:test';
import assert from 'node:assert/strict';
import { rateLimitKey, withinRateLimit } from '../src/rateLimit.js';

test('rateLimitKey uses the edge-supplied client IP', () => {
  const request = new Request('https://example.test/', { headers: { 'CF-Connecting-IP': '203.0.113.7' } });
  assert.equal(rateLimitKey(request), '203.0.113.7');
});

test('rateLimitKey falls back to a shared local bucket without the edge header', () => {
  assert.equal(rateLimitKey(new Request('https://example.test/')), 'local');
});

test('withinRateLimit passes the key through to the binding and honours its verdict', async () => {
  const seen = [];
  const limiter = {
    async limit(options) {
      seen.push(options);
      return { success: false };
    },
  };
  assert.equal(await withinRateLimit(limiter, '203.0.113.7'), false);
  assert.deepEqual(seen, [{ key: '203.0.113.7' }]);
});

test('withinRateLimit allows the request when the binding reports success', async () => {
  const limiter = { async limit() { return { success: true }; } };
  assert.equal(await withinRateLimit(limiter, 'k'), true);
});

test('withinRateLimit fails open when no limiter is bound', async () => {
  assert.equal(await withinRateLimit(undefined, 'k'), true);
  assert.equal(await withinRateLimit({}, 'k'), true);
});

test('withinRateLimit fails open when the binding throws', async () => {
  const limiter = { async limit() { throw new Error('binding unavailable'); } };
  assert.equal(await withinRateLimit(limiter, 'k'), true);
});
