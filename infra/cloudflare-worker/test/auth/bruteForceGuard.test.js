import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  isLockedOut,
  stateAfterSuccess,
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

test('stateAfterSuccess clears the attempt row', () => {
  assert.equal(stateAfterSuccess(), null);
});
