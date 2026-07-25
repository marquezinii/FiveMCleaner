// Pure decision logic for login brute-force protection, separated from the
// D1 read/write so it can be unit tested without a database. A row shape
// mirrors the `login_attempts` table: { failed_count, first_failed_at,
// locked_until } or null when the IP has no recorded attempts yet.

export const MAX_FAILED_ATTEMPTS = 5;
export const FAILURE_WINDOW_MS = 15 * 60 * 1000;
export const LOCKOUT_DURATION_MS = 15 * 60 * 1000;

/** True when the given attempt row is currently locked out at `now`. */
export function isLockedOut(row, now) {
  if (!row || !row.locked_until) {
    return false;
  }

  return new Date(row.locked_until).getTime() > now.getTime();
}

/**
 * Computes the next `login_attempts` row after a failed login. Resets the
 * counter if the previous failure window has expired, so an attacker cannot
 * accumulate failures indefinitely across unrelated days.
 */
export function nextStateAfterFailure(row, now) {
  const nowIso = now.toISOString();

  if (!row) {
    return { failed_count: 1, first_failed_at: nowIso, locked_until: null };
  }

  const windowExpired = now.getTime() - new Date(row.first_failed_at).getTime() > FAILURE_WINDOW_MS;
  if (windowExpired) {
    return { failed_count: 1, first_failed_at: nowIso, locked_until: null };
  }

  const failedCount = row.failed_count + 1;
  const lockedUntil = failedCount >= MAX_FAILED_ATTEMPTS
    ? new Date(now.getTime() + LOCKOUT_DURATION_MS).toISOString()
    : null;
  return { failed_count: failedCount, first_failed_at: row.first_failed_at, locked_until: lockedUntil };
}

/** The row to write after a successful login: attempts are cleared. */
export function stateAfterSuccess() {
  return null;
}
