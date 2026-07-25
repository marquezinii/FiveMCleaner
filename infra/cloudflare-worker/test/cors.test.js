import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildCorsHeaders, withCorsHeaders } from '../src/cors.js';

test('buildCorsHeaders returns matching headers when origin equals the allowed origin', () => {
  const headers = buildCorsHeaders('http://localhost:8788', 'http://localhost:8788');

  assert.equal(headers['Access-Control-Allow-Origin'], 'http://localhost:8788');
  assert.equal(headers['Access-Control-Allow-Credentials'], 'true');
});

test('buildCorsHeaders returns nothing when the origin does not match', () => {
  assert.deepEqual(buildCorsHeaders('https://evil.example', 'http://localhost:8788'), {});
});

test('buildCorsHeaders returns nothing when no allowed origin is configured', () => {
  assert.deepEqual(buildCorsHeaders('http://localhost:8788', ''), {});
  assert.deepEqual(buildCorsHeaders('http://localhost:8788', undefined), {});
});

test('buildCorsHeaders returns nothing when the request has no Origin header', () => {
  assert.deepEqual(buildCorsHeaders(null, 'http://localhost:8788'), {});
});

test('buildCorsHeaders never reflects an arbitrary origin -- only the exact configured one', () => {
  // Guards against a common CORS bug: reflecting request Origin back
  // unconditionally would defeat allow-listing entirely once credentials
  // are involved.
  const headers = buildCorsHeaders('http://localhost:9999', 'http://localhost:8788');

  assert.equal(headers['Access-Control-Allow-Origin'], undefined);
});

test('withCorsHeaders adds headers without changing the response status or body', async () => {
  const original = new Response('hello', { status: 202 });

  const result = withCorsHeaders(original, { 'Access-Control-Allow-Origin': 'http://localhost:8788' });

  assert.equal(result.status, 202);
  assert.equal(result.headers.get('Access-Control-Allow-Origin'), 'http://localhost:8788');
  assert.equal(await result.text(), 'hello');
});

test('withCorsHeaders preserves existing headers already on the response', () => {
  const original = new Response(null, { headers: { 'Content-Type': 'application/json' } });

  const result = withCorsHeaders(original, { 'Access-Control-Allow-Origin': 'http://localhost:8788' });

  assert.equal(result.headers.get('Content-Type'), 'application/json');
});
