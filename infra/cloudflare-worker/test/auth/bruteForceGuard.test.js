import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  isLockedOut,
  nextStateAfterFailure,
  stateAfterSuccess,
  MAX_FAILED_ATTEMPTS,
} from '../../src/auth/bruteForceGuard.js';

test('isLockedOut is false when there is no attempt row yet', () => {
  assert.equal(isLockedOut(null, new Date()), false);
});

test('isLockedOut is false when locked_until is null', () => {
  const row = { failed_count: 3, first_failed_at: new Date().toISOString(), locked_until: null };
  assert.equal(isLockedOut(row, new Date()), false);
});

test('isLockedOut is true while locked_until is in the future', () => {
  const now = new Date('2026-01-01T00:00:00Z');
  const row = { failed_count: 5, first_failed_at: now.toISOString(), locked_until: '2026-01-01T00:10:00Z' };

  assert.equal(isLockedOut(row, now), true);
});

test('isLockedOut is false once locked_until has passed', () => {
  const row = { failed_count: 5, first_failed_at: '2026-01-01T00:00:00Z', locked_until: '2026-01-01T00:10:00Z' };
  const later = new Date('2026-01-01T00:11:00Z');

  assert.equal(isLockedOut(row, later), false);
});

test('nextStateAfterFailure starts a new window for a first failure', () => {
  const now = new Date('2026-01-01T00:00:00Z');

  const result = nextStateAfterFailure(null, now);

  assert.equal(result.failed_count, 1);
  assert.equal(result.first_failed_at, now.toISOString());
  assert.equal(result.locked_until, null);
});

test('nextStateAfterFailure increments the counter within the same window', () => {
  const firstFailedAt = new Date('2026-01-01T00:00:00Z');
  const now = new Date('2026-01-01T00:05:00Z');
  const row = { failed_count: 2, first_failed_at: firstFailedAt.toISOString(), locked_until: null };

  const result = nextStateAfterFailure(row, now);

  assert.equal(result.failed_count, 3);
  assert.equal(result.first_failed_at, firstFailedAt.toISOString());
  assert.equal(result.locked_until, null);
});

test('nextStateAfterFailure locks out once the max attempt count is reached', () => {
  const firstFailedAt = new Date('2026-01-01T00:00:00Z');
  const now = new Date('2026-01-01T00:05:00Z');
  const row = { failed_count: MAX_FAILED_ATTEMPTS - 1, first_failed_at: firstFailedAt.toISOString(), locked_until: null };

  const result = nextStateAfterFailure(row, now);

  assert.equal(result.failed_count, MAX_FAILED_ATTEMPTS);
  assert.ok(result.locked_until);
  assert.ok(new Date(result.locked_until).getTime() > now.getTime());
});

test('nextStateAfterFailure resets the counter once the failure window has expired', () => {
  const firstFailedAt = new Date('2026-01-01T00:00:00Z');
  const now = new Date('2026-01-01T01:00:00Z'); // well past the 15-minute window
  const row = { failed_count: MAX_FAILED_ATTEMPTS, first_failed_at: firstFailedAt.toISOString(), locked_until: null };

  const result = nextStateAfterFailure(row, now);

  assert.equal(result.failed_count, 1);
  assert.equal(result.first_failed_at, now.toISOString());
  assert.equal(result.locked_until, null);
});

test('stateAfterSuccess clears the attempt row', () => {
  assert.equal(stateAfterSuccess(), null);
});
