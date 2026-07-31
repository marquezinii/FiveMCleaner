import test from 'node:test';
import assert from 'node:assert/strict';
import { validateUpdaterEvent } from '../../src/updaterEvents/validateSubmission.js';

test('accepts a closed updater event and rejects free text', () => {
  const valid = { eventId: 'a'.repeat(32), stage: 'rollback', outcome: 'rolled-back', errorCode: 'health-timeout', previousVersion: '1.1.3', candidateVersion: '1.2.0', environment: 'Production' };
  assert.deepEqual(validateUpdaterEvent(valid), valid);
  assert.equal(validateUpdaterEvent({ ...valid, details: 'personal path' }), null);
});
