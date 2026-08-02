// Pure, dependency-free validation for one telemetry event. Kept separate
// from index.js so it can be unit tested without Miniflare/wrangler and
// without a D1 binding. Mirrors FiveMCleaner.App.Services.TelemetryEventValidator
// on the .NET side -- the Worker never trusts client-side validation alone.

import { ALLOWED_ENVIRONMENTS } from './environments.js';

export const ALLOWED_EVENT_NAMES = new Set([
  'optimization-completed',
  'optimization-failed',
  'optimization-cancelled',
]);

export const ALLOWED_ERROR_CATEGORIES = new Set([
  'cancelled',
  'timeout',
  'access-denied',
  'io',
  'invalid-data',
  'unexpected',
]);

export const ALLOWED_PROFILES = new Set(['Light', 'Balanced', 'Aggressive']);

export const ALLOWED_RAM_BUCKETS_GIB = new Set([2, 4, 8, 16, 32, 64, 128, 256]);

export const MAX_APP_VERSION_LENGTH = 32;
export const MAX_SHORT_FIELD_LENGTH = 128;
export const MAX_ACTION_IDS = 30;

// Mirrors the same 24h clamp already applied client-side in
// FormSubmitAnonymousTelemetryService / CloudflareTelemetryTransport.
export const MAX_EXECUTION_TIME_MS = 86_400_000;

export const MAX_BATCH_SIZE = 50;

// Old and future clients must not silently lose telemetry if they omit this
// classification field. The production Worker is the only public endpoint,
// while development clients explicitly send "Development".

const ACTION_ID_PATTERN = /^[A-Za-z0-9.-]+$/;
const CONTROL_CHARACTER_PATTERN = /[\x00-\x1F\x7F]/;

function isValidShortField(value) {
  if (value === undefined || value === null) {
    return true;
  }

  return (
    typeof value === 'string' &&
    value.length <= MAX_SHORT_FIELD_LENGTH &&
    !CONTROL_CHARACTER_PATTERN.test(value)
  );
}

/**
 * Validates and normalizes one event from the request body. Returns `null`
 * when the event does not match the closed schema -- never throws, and
 * never trusts a field it did not explicitly check.
 */
export function validateEvent(event) {
  if (typeof event !== 'object' || event === null) {
    return null;
  }

  const {
    eventName,
    executionTimeMs,
    appVersion,
    errorCategory,
    environment: submittedEnvironment,
    osVersion,
    systemArchitecture,
    cpuModel,
    gpuModel,
    ramBucketGiB,
    profile,
    actionIds,
  } = event;

  if (typeof eventName !== 'string' || !ALLOWED_EVENT_NAMES.has(eventName)) {
    return null;
  }

  if (
    typeof executionTimeMs !== 'number' ||
    !Number.isFinite(executionTimeMs) ||
    executionTimeMs < 0 ||
    executionTimeMs > MAX_EXECUTION_TIME_MS
  ) {
    return null;
  }

  if (
    typeof appVersion !== 'string' ||
    appVersion.length === 0 ||
    appVersion.length > MAX_APP_VERSION_LENGTH
  ) {
    return null;
  }

  if (
    errorCategory !== undefined &&
    errorCategory !== null &&
    (typeof errorCategory !== 'string' || !ALLOWED_ERROR_CATEGORIES.has(errorCategory))
  ) {
    return null;
  }

  const environment = submittedEnvironment === undefined ? 'Production' : submittedEnvironment;

  if (typeof environment !== 'string' || !ALLOWED_ENVIRONMENTS.has(environment)) {
    return null;
  }

  if (!isValidShortField(osVersion) || !isValidShortField(systemArchitecture)) {
    return null;
  }

  if (!isValidShortField(cpuModel) || !isValidShortField(gpuModel)) {
    return null;
  }

  if (
    ramBucketGiB !== undefined &&
    ramBucketGiB !== null &&
    !ALLOWED_RAM_BUCKETS_GIB.has(ramBucketGiB)
  ) {
    return null;
  }

  if (profile !== undefined && profile !== null && !ALLOWED_PROFILES.has(profile)) {
    return null;
  }

  let normalizedActionIds = [];
  if (actionIds !== undefined && actionIds !== null) {
    if (!Array.isArray(actionIds) || actionIds.length > MAX_ACTION_IDS) {
      return null;
    }

    for (const actionId of actionIds) {
      if (
        typeof actionId !== 'string' ||
        actionId.length === 0 ||
        actionId.length > MAX_SHORT_FIELD_LENGTH ||
        !ACTION_ID_PATTERN.test(actionId)
      ) {
        return null;
      }
    }

    normalizedActionIds = actionIds;
  }

  return {
    eventName,
    executionTimeMs: Math.trunc(executionTimeMs),
    appVersion,
    errorCategory: errorCategory ?? null,
    environment,
    osVersion: osVersion ?? null,
    systemArchitecture: systemArchitecture ?? null,
    cpuModel: cpuModel ?? null,
    gpuModel: gpuModel ?? null,
    ramBucketGiB: ramBucketGiB ?? null,
    profile: profile ?? null,
    actionIds: normalizedActionIds,
  };
}

/**
 * Validates a whole request body, which may be a single event or a batch
 * (array) of events. Returns `null` if the batch shape or size is invalid,
 * or if any single event fails validation -- an all-or-nothing batch never
 * partially inserts.
 */
export function validateBatch(payload) {
  const events = Array.isArray(payload) ? payload : [payload];
  if (events.length === 0 || events.length > MAX_BATCH_SIZE) {
    return null;
  }

  const validated = [];
  for (const event of events) {
    const result = validateEvent(event);
    if (result === null) {
      return null;
    }

    validated.push(result);
  }

  return validated;
}
