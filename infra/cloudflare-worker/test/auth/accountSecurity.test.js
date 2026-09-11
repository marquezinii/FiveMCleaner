import test from 'node:test';
import assert from 'node:assert/strict';
import {
  deleteRecoveryCodes,
  generateRecoveryCodes,
  isRecentAuthentication,
  recoveryRateLimitKey,
  replaceRecoveryCodes,
  tokenPassesAccountCutoff,
  validateEnrollmentId,
  validateRecoveryRequest,
} from '../../src/auth/accountSecurity.js';

const SECRET = 'test-only-secret-with-at-least-32-characters';

test('recent authentication accepts a five-minute window and rejects missing, stale, or future claims', () => {
  assert.equal(isRecentAuthentication({ authTime: 700 }, 1000), true);
  assert.equal(isRecentAuthentication({ authTime: 699 }, 1000), false);
  assert.equal(isRecentAuthentication({ authTime: 1061 }, 1000), false);
  assert.equal(isRecentAuthentication({ authTime: null }, 1000), false);
});

test('recovery request validation is strict and normalizes the human code', () => {
  assert.deepEqual(validateRecoveryRequest({
    mfaPendingCredential: 'pending-credential-long-enough',
    mfaEnrollmentId: 'totp-enrollment_1',
    recoveryCode: 'abcde-fghjk',
  }), {
    mfaPendingCredential: 'pending-credential-long-enough',
    mfaEnrollmentId: 'totp-enrollment_1',
    recoveryCode: 'ABCDEFGHJK',
  });
  assert.equal(validateRecoveryRequest({
    mfaPendingCredential: 'short', mfaEnrollmentId: 'ok', recoveryCode: 'ABCDE-FGHJK',
  }), null);
  assert.equal(validateRecoveryRequest({
    mfaPendingCredential: 'pending-credential-long-enough', mfaEnrollmentId: '../bad', recoveryCode: 'ABCDE-FGHJK',
  }), null);
  assert.equal(validateRecoveryRequest({
    mfaPendingCredential: 'pending-credential-long-enough', mfaEnrollmentId: 'totp-1',
    recoveryCode: 'ABCDE-FGHJK', uid: 'client-controlled',
  }), null);
  assert.equal(validateEnrollmentId('totp-enrollment_1'), 'totp-enrollment_1');
  assert.equal(validateEnrollmentId(''), null);
});

test('recovery codes use a fixed unambiguous format', () => {
  const bytes = Uint8Array.from({ length: 100 }, (_, index) => index);
  const codes = generateRecoveryCodes(bytes);
  assert.equal(codes.length, 10);
  for (const code of codes) assert.match(code, /^[23456789A-HJ-NP-Z]{5}-[23456789A-HJ-NP-Z]{5}$/);
});

function captureDb(cutoff = null) {
  const statements = [];
  const batches = [];
  return {
    statements,
    batches,
    prepare(sql) {
      return {
        bind(...params) {
          const statement = { sql, params };
          statements.push(statement);
          return {
            ...statement,
            async run() { return { meta: { changes: 1 } }; },
            async first() { return cutoff === null ? null : { valid_after: cutoff }; },
          };
        },
      };
    },
    async batch(items) { batches.push(items); },
  };
}

test('replacement persists HMACs only and atomically replaces the prior generation', async () => {
  const db = captureDb();
  const codes = generateRecoveryCodes(Uint8Array.from({ length: 100 }, (_, index) => index));
  await replaceRecoveryCodes(db, 'uid-1', 'enrollment-1', SECRET, codes, new Date('2026-01-01T00:00:00Z'));
  assert.equal(db.batches.length, 1);
  assert.equal(db.batches[0].length, 11);
  const serialized = JSON.stringify(db.batches[0]);
  assert.doesNotMatch(serialized, /23456-789AB/);
  assert.doesNotMatch(serialized, /enrollment-1/);
  assert.match(db.batches[0][1].params[1], /^[a-f0-9]{64}$/);
  assert.match(db.batches[0][1].params[2], /^[a-f0-9]{64}$/);
});

test('recovery deletion is scoped by uid and an enrollment HMAC', async () => {
  const db = captureDb();
  await deleteRecoveryCodes(db, 'uid-1', 'enrollment-1', SECRET);
  assert.equal(db.statements[0].params[0], 'uid-1');
  assert.match(db.statements[0].params[1], /^[a-f0-9]{64}$/);
  assert.doesNotMatch(JSON.stringify(db.statements[0]), /enrollment-1/);
});

test('rate-limit keys stay stable across pending credentials and account cutoffs use auth_time', async () => {
  const key = await recoveryRateLimitKey(SECRET, 'enrollment-1', '203.0.113.4');
  const sameKey = await recoveryRateLimitKey(SECRET, 'enrollment-1', '203.0.113.4');
  assert.match(key, /^account-recovery:[a-f0-9]{64}$/);
  assert.equal(key, sameKey);
  assert.doesNotMatch(key, /enrollment|203\.0\.113/);
  assert.equal(await tokenPassesAccountCutoff(captureDb(100), { uid: 'u', authTime: 101 }), true);
  assert.equal(await tokenPassesAccountCutoff(captureDb(100), { uid: 'u', authTime: 100 }), false);
  assert.equal(await tokenPassesAccountCutoff(captureDb(100), { uid: 'u', authTime: 99 }), false);
  assert.equal(await tokenPassesAccountCutoff(captureDb(100), { uid: 'u', issuedAt: 200 }), false);
});
