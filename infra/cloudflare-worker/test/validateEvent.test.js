import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateEvent, validateBatch, MAX_BATCH_SIZE } from '../src/validateEvent.js';

function validEvent(overrides = {}) {
  return {
    eventName: 'optimization-completed',
    executionTimeMs: 18342,
    appVersion: '1.0.3',
    errorCategory: null,
    environment: 'Production',
    ...overrides,
  };
}

test('validateEvent accepts a well-formed completed event', () => {
  const result = validateEvent(validEvent());
  assert.deepEqual(result, {
    eventName: 'optimization-completed',
    executionTimeMs: 18342,
    appVersion: '1.0.3',
    errorCategory: null,
    environment: 'Production',
  });
});

test('validateEvent accepts a failed event with an allowlisted error category', () => {
  const result = validateEvent(
    validEvent({ eventName: 'optimization-failed', errorCategory: 'timeout' }),
  );
  assert.equal(result.errorCategory, 'timeout');
});

test('validateEvent rejects an unknown event name', () => {
  assert.equal(validateEvent(validEvent({ eventName: 'something-else' })), null);
});

test('validateEvent rejects an unknown error category', () => {
  assert.equal(
    validateEvent(validEvent({ eventName: 'optimization-failed', errorCategory: 'sql-injection' })),
    null,
  );
});

test('validateEvent rejects a negative execution time', () => {
  assert.equal(validateEvent(validEvent({ executionTimeMs: -1 })), null);
});

test('validateEvent rejects an execution time over the 24h clamp', () => {
  assert.equal(validateEvent(validEvent({ executionTimeMs: 86_400_001 })), null);
});

test('validateEvent rejects a non-finite execution time', () => {
  assert.equal(validateEvent(validEvent({ executionTimeMs: Number.POSITIVE_INFINITY })), null);
  assert.equal(validateEvent(validEvent({ executionTimeMs: Number.NaN })), null);
});

test('validateEvent rejects an empty or overly long app version', () => {
  assert.equal(validateEvent(validEvent({ appVersion: '' })), null);
  assert.equal(validateEvent(validEvent({ appVersion: 'x'.repeat(33) })), null);
});

test('validateEvent rejects an unknown environment', () => {
  assert.equal(validateEvent(validEvent({ environment: 'Staging' })), null);
});

test('validateEvent rejects a payload that is not an object', () => {
  assert.equal(validateEvent('not-an-object'), null);
  assert.equal(validateEvent(null), null);
  assert.equal(validateEvent(42), null);
});

test('validateEvent rejects a payload carrying unexpected fields silently by ignoring them, but still validates the closed schema', () => {
  const result = validateEvent(validEvent({ userPath: 'C:\\Users\\someone\\file.txt' }));
  assert.ok(result);
  assert.equal('userPath' in result, false);
});

test('validateBatch accepts a single event wrapped as one item', () => {
  const result = validateBatch(validEvent());
  assert.equal(result.length, 1);
});

test('validateBatch accepts an array of valid events', () => {
  const result = validateBatch([validEvent(), validEvent({ environment: 'Development' })]);
  assert.equal(result.length, 2);
});

test('validateBatch rejects an empty array', () => {
  assert.equal(validateBatch([]), null);
});

test('validateBatch rejects a batch larger than the maximum size', () => {
  const events = Array.from({ length: MAX_BATCH_SIZE + 1 }, () => validEvent());
  assert.equal(validateBatch(events), null);
});

test('validateBatch rejects the whole batch when any single event is invalid', () => {
  const events = [validEvent(), validEvent({ eventName: 'not-allowed' })];
  assert.equal(validateBatch(events), null);
});
