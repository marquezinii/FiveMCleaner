import { test } from 'node:test';
import assert from 'node:assert/strict';
import { hashPassword, verifyPassword, generateSessionId, hashIp } from '../../src/auth/crypto.js';

test('hashPassword produces a self-contained pbkdf2 string with the requested iterations', async () => {
  const hash = await hashPassword('correct horse battery staple', 1000);

  const parts = hash.split('$');
  assert.equal(parts.length, 4);
  assert.equal(parts[0], 'pbkdf2');
  assert.equal(parts[1], '1000');
});

test('verifyPassword accepts the correct password against its own hash', async () => {
  const hash = await hashPassword('correct horse battery staple', 1000);

  assert.equal(await verifyPassword('correct horse battery staple', hash), true);
});

test('verifyPassword rejects a wrong password', async () => {
  const hash = await hashPassword('correct horse battery staple', 1000);

  assert.equal(await verifyPassword('wrong password', hash), false);
});

test('verifyPassword rejects a malformed stored hash instead of throwing', async () => {
  assert.equal(await verifyPassword('anything', 'not-a-real-hash'), false);
  assert.equal(await verifyPassword('anything', ''), false);
  assert.equal(await verifyPassword('anything', undefined), false);
});

test('verifyPassword rejects a hash with a non-numeric iteration count', async () => {
  assert.equal(await verifyPassword('anything', 'pbkdf2$not-a-number$c2FsdA==$aGFzaA=='), false);
});

test('two hashes of the same password use different salts and therefore differ', async () => {
  const first = await hashPassword('same password', 1000);
  const second = await hashPassword('same password', 1000);

  assert.notEqual(first, second);
  assert.equal(await verifyPassword('same password', first), true);
  assert.equal(await verifyPassword('same password', second), true);
});

test('generateSessionId returns a sufficiently long, URL-safe, unique value', () => {
  const first = generateSessionId();
  const second = generateSessionId();

  assert.notEqual(first, second);
  assert.ok(first.length >= 32);
  assert.match(first, /^[A-Za-z0-9_-]+$/);
});

test('hashIp is deterministic for the same IP and secret', async () => {
  const first = await hashIp('203.0.113.1', 'test-secret');
  const second = await hashIp('203.0.113.1', 'test-secret');

  assert.equal(first, second);
});

test('hashIp differs for different IPs or different secrets', async () => {
  const baseline = await hashIp('203.0.113.1', 'test-secret');

  assert.notEqual(await hashIp('203.0.113.2', 'test-secret'), baseline);
  assert.notEqual(await hashIp('203.0.113.1', 'other-secret'), baseline);
});
