import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  MAX_LIVE_ALERT_MESSAGE_LENGTH,
  validateLiveAlertUpdate,
} from '../../src/liveAlert/validateSubmission.js';

test('validateLiveAlertUpdate accepts a message plus active:true and trims it', () => {
  const result = validateLiveAlertUpdate({ message: '  Entre no Discord oficial  ', active: true });
  assert.deepEqual(result, { message: 'Entre no Discord oficial', active: true });
});

test('validateLiveAlertUpdate accepts active:false with no message (deactivate)', () => {
  const result = validateLiveAlertUpdate({ active: false });
  assert.deepEqual(result, { active: false });
});

test('validateLiveAlertUpdate rejects active:true with an empty message', () => {
  assert.equal(validateLiveAlertUpdate({ message: '   ', active: true }), null);
});

test('validateLiveAlertUpdate accepts active:false with an empty message', () => {
  const result = validateLiveAlertUpdate({ message: '   ', active: false });
  assert.deepEqual(result, { message: '', active: false });
});

test('validateLiveAlertUpdate rejects a message over the length limit', () => {
  const tooLong = 'x'.repeat(MAX_LIVE_ALERT_MESSAGE_LENGTH + 1);
  assert.equal(validateLiveAlertUpdate({ message: tooLong, active: true }), null);
});

test('validateLiveAlertUpdate accepts a message exactly at the length limit', () => {
  const atLimit = 'x'.repeat(MAX_LIVE_ALERT_MESSAGE_LENGTH);
  const result = validateLiveAlertUpdate({ message: atLimit, active: true });
  assert.deepEqual(result, { message: atLimit, active: true });
});

test('validateLiveAlertUpdate rejects a missing active flag', () => {
  assert.equal(validateLiveAlertUpdate({ message: 'oi' }), null);
});

test('validateLiveAlertUpdate rejects a non-string message', () => {
  assert.equal(validateLiveAlertUpdate({ message: 123, active: true }), null);
});

test('validateLiveAlertUpdate rejects a non-object payload', () => {
  assert.equal(validateLiveAlertUpdate(null), null);
  assert.equal(validateLiveAlertUpdate('nope'), null);
});
