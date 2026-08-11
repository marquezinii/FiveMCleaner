import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { buildFailedLoginMutation } from '../../src/auth/passwordAuthProvider.js';
import { MAX_FAILED_ATTEMPTS } from '../../src/auth/bruteForceGuard.js';

function createDatabase() {
  const db = new DatabaseSync(':memory:');
  db.exec(`CREATE TABLE login_attempts (
    ip_hash TEXT PRIMARY KEY,
    failed_count INTEGER NOT NULL,
    first_failed_at TEXT NOT NULL,
    locked_until TEXT NULL
  )`);
  return db;
}

function recordFailure(db, ipHash, now) {
  const mutation = buildFailedLoginMutation(ipHash, now);
  db.prepare(mutation.sql).run(...mutation.bindings);
}

test('failed login mutation increments the database value instead of a stale read', () => {
  const db = createDatabase();
  const now = new Date('2026-08-10T12:00:00.000Z');

  recordFailure(db, 'same-ip', now);
  recordFailure(db, 'same-ip', now);

  const row = db.prepare('SELECT * FROM login_attempts WHERE ip_hash = ?').get('same-ip');
  assert.equal(row.failed_count, 2);
  db.close();
});

test('failed login mutation atomically reaches lockout and resets an expired window', () => {
  const db = createDatabase();
  const now = new Date('2026-08-10T12:00:00.000Z');

  for (let count = 0; count < MAX_FAILED_ATTEMPTS; count += 1) {
    recordFailure(db, 'same-ip', now);
  }
  let row = db.prepare('SELECT * FROM login_attempts WHERE ip_hash = ?').get('same-ip');
  assert.equal(row.failed_count, MAX_FAILED_ATTEMPTS);
  assert.ok(row.locked_until);

  recordFailure(db, 'same-ip', new Date('2026-08-10T13:00:00.000Z'));
  row = db.prepare('SELECT * FROM login_attempts WHERE ip_hash = ?').get('same-ip');
  assert.equal(row.failed_count, 1);
  assert.equal(row.locked_until, null);
  db.close();
});
