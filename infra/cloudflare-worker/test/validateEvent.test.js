import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateEvent, validateBatch, MAX_BATCH_SIZE, MAX_ACTION_IDS } from '../src/validateEvent.js';

function validEvent(overrides = {}) {
  return {
    eventName: 'optimization-completed',
    executionTimeMs: 18342,
    appVersion: '1.0.4',
    errorCategory: null,
    environment: 'Production',
    osVersion: 'Windows 11',
    systemArchitecture: 'x64',
    cpuModel: 'AMD Ryzen 5 5600X',
    gpuModel: 'NVIDIA GeForce RTX 5070',
    ramBucketGiB: 32,
    profile: 'Balanced',
    actionIds: ['fivem.legacy.cache.repair', 'windows.power-plan.session'],
    ...overrides,
  };
}

test('validateEvent accepts a well-formed completed event with the full hardware profile', () => {
  const result = validateEvent(validEvent());
  assert.deepEqual(result, {
    eventName: 'optimization-completed',
    executionTimeMs: 18342,
    appVersion: '1.0.4',
    errorCategory: null,
    environment: 'Production',
    osVersion: 'Windows 11',
    systemArchitecture: 'x64',
    cpuModel: 'AMD Ryzen 5 5600X',
    gpuModel: 'NVIDIA GeForce RTX 5070',
    ramBucketGiB: 32,
    profile: 'Balanced',
    actionIds: ['fivem.legacy.cache.repair', 'windows.power-plan.session'],
    fiveMInstallDetected: null,
    gtaEdition: null,
    optimizationTargetCount: null,
    windowsBuild: null,
    diskType: null,
    freeSpaceGiBBucket: null,
    runTimestamp: null,
    daysSinceLastRunBucket: null,
    backupCreated: null,
    backupRestored: null,
    elevationUsed: null,
    processCountAtStart: null,
  });
});

test('validateEvent accepts an event without any of the optional hardware fields', () => {
  const result = validateEvent({
    eventName: 'optimization-cancelled',
    executionTimeMs: 0,
    appVersion: '1.0.4',
    environment: 'Development',
  });

  assert.ok(result);
  assert.equal(result.osVersion, null);
  assert.equal(result.cpuModel, null);
  assert.equal(result.ramBucketGiB, null);
  assert.deepEqual(result.actionIds, []);
});

test('validateEvent accepts a failed event with an allowlisted error category', () => {
  const result = validateEvent(
    validEvent({ eventName: 'optimization-failed', errorCategory: 'timeout' }),
  );
  assert.equal(result.errorCategory, 'timeout');
});

test('validateEvent defaults a missing environment to Production for compatibility', () => {
  const result = validateEvent({
    eventName: 'optimization-completed',
    executionTimeMs: 100,
    appVersion: '1.1.1',
  });

  assert.equal(result.environment, 'Production');
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

test('validateEvent still rejects a null environment', () => {
  assert.equal(validateEvent({
    eventName: 'optimization-completed',
    executionTimeMs: 100,
    appVersion: '1.1.1',
    environment: null,
  }), null);
});

test('validateEvent rejects a payload that is not an object', () => {
  assert.equal(validateEvent('not-an-object'), null);
  assert.equal(validateEvent(null), null);
  assert.equal(validateEvent(42), null);
});

test('validateEvent rejects an unknown RAM bucket', () => {
  assert.equal(validateEvent(validEvent({ ramBucketGiB: 3 })), null);
});

test('validateEvent rejects an unknown profile', () => {
  assert.equal(validateEvent(validEvent({ profile: 'Ultra' })), null);
});

test('validateEvent rejects a CPU/GPU model containing control characters (never free text/paths)', () => {
  assert.equal(validateEvent(validEvent({ cpuModel: 'AMD\nRyzen' })), null);
  assert.equal(validateEvent(validEvent({ gpuModel: 'C:\\Users\\someone\\file.txt\x00' })), null);
});

test('validateEvent rejects an action ID with characters outside the allowlisted pattern', () => {
  assert.equal(validateEvent(validEvent({ actionIds: ['C:\\Users\\someone\\file.txt'] })), null);
  assert.equal(validateEvent(validEvent({ actionIds: ['has spaces'] })), null);
});

test('validateEvent rejects more action IDs than the maximum allowed', () => {
  const tooMany = Array.from({ length: MAX_ACTION_IDS + 1 }, (_, i) => `action.${i}`);
  assert.equal(validateEvent(validEvent({ actionIds: tooMany })), null);
});

test('validateEvent rejects actionIds that is not an array', () => {
  assert.equal(validateEvent(validEvent({ actionIds: 'fivem.legacy.cache.repair' })), null);
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

// --- v5: expanded diagnostic fields ---

test('validateEvent accepts v5 fields with valid values', () => {
  const result = validateEvent(validEvent({
    fiveMInstallDetected: true,
    gtaEdition: 'Legacy',
    optimizationTargetCount: 150,
    windowsBuild: 22621,
    diskType: 'SSD',
    freeSpaceGiBBucket: 100,
    runTimestamp: '2026-08-15T10:30:00Z',
    daysSinceLastRunBucket: 2,
    backupCreated: true,
    backupRestored: false,
    elevationUsed: false,
    processCountAtStart: 1,
  }));

  assert.ok(result);
  assert.equal(result.fiveMInstallDetected, true);
  assert.equal(result.gtaEdition, 'Legacy');
  assert.equal(result.optimizationTargetCount, 150);
  assert.equal(result.windowsBuild, 22621);
  assert.equal(result.diskType, 'SSD');
  assert.equal(result.freeSpaceGiBBucket, 100);
  assert.equal(result.runTimestamp, '2026-08-15T10:30:00Z');
  assert.equal(result.daysSinceLastRunBucket, 2);
  assert.equal(result.backupCreated, true);
  assert.equal(result.backupRestored, false);
  assert.equal(result.elevationUsed, false);
  assert.equal(result.processCountAtStart, 1);
});

test('validateEvent accepts null/omitted v5 fields for backward compatibility with older clients', () => {
  const result = validateEvent(validEvent());
  assert.equal(result.fiveMInstallDetected, null);
  assert.equal(result.gtaEdition, null);
  assert.equal(result.optimizationTargetCount, null);
  assert.equal(result.windowsBuild, null);
  assert.equal(result.diskType, null);
  assert.equal(result.freeSpaceGiBBucket, null);
  assert.equal(result.runTimestamp, null);
  assert.equal(result.daysSinceLastRunBucket, null);
  assert.equal(result.backupCreated, null);
  assert.equal(result.backupRestored, null);
  assert.equal(result.elevationUsed, null);
  assert.equal(result.processCountAtStart, null);
});

test('validateEvent rejects an invalid gtaEdition', () => {
  assert.equal(validateEvent(validEvent({ gtaEdition: 'Steam' })), null);
});

test('validateEvent rejects an invalid diskType', () => {
  assert.equal(validateEvent(validEvent({ diskType: 'SATA' })), null);
});

test('validateEvent rejects freeSpaceGiBBucket outside the allowed bucket set', () => {
  assert.equal(validateEvent(validEvent({ freeSpaceGiBBucket: 25 })), null);
});

test('validateEvent rejects daysSinceLastRunBucket outside the allowed bucket set', () => {
  assert.equal(validateEvent(validEvent({ daysSinceLastRunBucket: 15 })), null);
});

test('validateEvent rejects processCountAtStart outside the allowed bucket set', () => {
  assert.equal(validateEvent(validEvent({ processCountAtStart: 2 })), null);
});

test('validateEvent rejects optimizationTargetCount over the maximum', () => {
  assert.equal(validateEvent(validEvent({ optimizationTargetCount: 100_001 })), null);
});

test('validateEvent rejects a non-integer optimizationTargetCount', () => {
  assert.equal(validateEvent(validEvent({ optimizationTargetCount: 1.5 })), null);
});

test('validateEvent rejects windowsBuild over the maximum', () => {
  assert.equal(validateEvent(validEvent({ windowsBuild: 100_000 })), null);
});

test('validateEvent rejects a non-boolean fiveMInstallDetected', () => {
  assert.equal(validateEvent(validEvent({ fiveMInstallDetected: 'yes' })), null);
});

test('validateEvent rejects a non-boolean backupCreated', () => {
  assert.equal(validateEvent(validEvent({ backupCreated: 1 })), null);
});

test('validateEvent rejects an unparseable runTimestamp', () => {
  assert.equal(validateEvent(validEvent({ runTimestamp: 'not-a-date' })), null);
});

test('validateEvent accepts a valid runTimestamp', () => {
  assert.ok(validateEvent(validEvent({ runTimestamp: '2026-01-01T00:00:00.000Z' })));
});
