import test from 'node:test';
import assert from 'node:assert/strict';
import { validateUpdaterEvent } from '../../src/updaterEvents/validateSubmission.js';

test('accepts a closed updater event and rejects free text', () => {
  const valid = { eventId: 'a'.repeat(32), stage: 'rollback', outcome: 'rolled-back', errorCode: 'health-timeout', previousVersion: '1.1.3', candidateVersion: '1.2.0', environment: 'Production' };
  assert.deepEqual(validateUpdaterEvent(valid), valid);
  assert.equal(validateUpdaterEvent({ ...valid, details: 'personal path' }), null);
});

test('rejects an event without a string errorCode', () => {
  const base = { eventId: 'a'.repeat(32), stage: 'rollback', outcome: 'rolled-back', errorCode: 'health-timeout', previousVersion: '1.1.3', candidateVersion: '1.2.0', environment: 'Production' };
  const missing = { ...base };
  delete missing.errorCode;
  assert.equal(validateUpdaterEvent(missing), null);
  assert.equal(validateUpdaterEvent({ ...base, errorCode: null }), null);
  assert.equal(validateUpdaterEvent({ ...base, errorCode: 42 }), null);
});
