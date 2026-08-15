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

/** The row to write after a successful login: attempts are cleared. */
export function stateAfterSuccess() {
  return null;
}
