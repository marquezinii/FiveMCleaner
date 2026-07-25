import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  isSessionValid,
  createSessionRow,
  readSessionCookie,
  buildSessionCookie,
  buildExpiredSessionCookie,
  SESSION_COOKIE_NAME,
} from '../../src/auth/sessionStore.js';

test('isSessionValid is false when there is no session row', () => {
  assert.equal(isSessionValid(null, new Date()), false);
});

test('isSessionValid is false when the session was revoked', () => {
  const row = {
    id: 'abc',
    created_at: '2026-01-01T00:00:00Z',
    expires_at: '2026-01-02T00:00:00Z',
    revoked_at: '2026-01-01T00:05:00Z',
  };

  assert.equal(isSessionValid(row, new Date('2026-01-01T00:06:00Z')), false);
});

test('isSessionValid is true before expiry and false after', () => {
  const row = { id: 'abc', created_at: '2026-01-01T00:00:00Z', expires_at: '2026-01-01T12:00:00Z', revoked_at: null };

  assert.equal(isSessionValid(row, new Date('2026-01-01T11:59:00Z')), true);
  assert.equal(isSessionValid(row, new Date('2026-01-01T12:01:00Z')), false);
});

test('createSessionRow sets expires_at 12 hours after now by default', () => {
  const now = new Date('2026-01-01T00:00:00Z');

  const row = createSessionRow(now);

  assert.equal(row.created_at, now.toISOString());
  assert.equal(row.expires_at, '2026-01-01T12:00:00.000Z');
  assert.equal(row.revoked_at, null);
  assert.ok(row.id.length >= 32);
});

test('readSessionCookie extracts the session cookie value among several cookies', () => {
  const header = `other=1; ${SESSION_COOKIE_NAME}=the-session-id; another=2`;

  assert.equal(readSessionCookie(header), 'the-session-id');
});

test('readSessionCookie returns null when the cookie is absent', () => {
  assert.equal(readSessionCookie('other=1'), null);
  assert.equal(readSessionCookie(null), null);
  assert.equal(readSessionCookie(''), null);
});

test('buildSessionCookie marks the cookie HttpOnly, Secure, and SameSite=Strict', () => {
  const cookie = buildSessionCookie('the-session-id', '2026-01-01T12:00:00Z');

  assert.match(cookie, new RegExp(`^${SESSION_COOKIE_NAME}=the-session-id;`));
  assert.match(cookie, /HttpOnly/);
  assert.match(cookie, /Secure/);
  assert.match(cookie, /SameSite=Strict/);
});

test('buildExpiredSessionCookie clears the cookie in the past', () => {
  const cookie = buildExpiredSessionCookie();

  assert.match(cookie, /Expires=Thu, 01 Jan 1970/);
});
