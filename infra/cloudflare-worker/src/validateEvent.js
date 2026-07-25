// Pure, dependency-free validation for one telemetry event. Kept separate
// from index.js so it can be unit tested without Miniflare/wrangler and
// without a D1 binding.

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

export const ALLOWED_ENVIRONMENTS = new Set(['Development', 'Production']);

export const MAX_APP_VERSION_LENGTH = 32;

// Mirrors the same 24h clamp already applied client-side in
// FormSubmitAnonymousTelemetryService.
export const MAX_EXECUTION_TIME_MS = 86_400_000;

export const MAX_BATCH_SIZE = 50;

/**
 * Validates and normalizes one event from the request body. Returns `null`
 * when the event does not match the closed schema -- never throws, and
 * never trusts a field it did not explicitly check.
 */
export function validateEvent(event) {
  if (typeof event !== 'object' || event === null) {
    return null;
  }

  const { eventName, executionTimeMs, appVersion, errorCategory, environment } = event;

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

  if (typeof environment !== 'string' || !ALLOWED_ENVIRONMENTS.has(environment)) {
    return null;
  }

  return {
    eventName,
    executionTimeMs: Math.trunc(executionTimeMs),
    appVersion,
    errorCategory: errorCategory ?? null,
    environment,
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
