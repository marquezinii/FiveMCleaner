// Pure session validity logic, separated from the D1 read/write so it can
// be unit tested without a database. A row shape mirrors the
// `admin_sessions` table: { id, created_at, expires_at, revoked_at }.

import { generateSessionId } from './crypto.js';

export const SESSION_DURATION_MS = 12 * 60 * 60 * 1000; // 12h
export const SESSION_COOKIE_NAME = 'fmc_dashboard_session';

/** True when the given session row is currently valid at `now`. */
export function isSessionValid(row, now) {
  if (!row || row.revoked_at) {
    return false;
  }

  return new Date(row.expires_at).getTime() > now.getTime();
}

/** Builds a fresh `admin_sessions` row for a just-authenticated login. */
export function createSessionRow(now, durationMs = SESSION_DURATION_MS) {
  return {
    id: generateSessionId(),
    created_at: now.toISOString(),
    expires_at: new Date(now.getTime() + durationMs).toISOString(),
    revoked_at: null,
  };
}

/** Parses the session cookie value out of a raw `Cookie` request header. */
export function readSessionCookie(cookieHeader) {
  if (!cookieHeader) {
    return null;
  }

  for (const part of cookieHeader.split(';')) {
    const [name, ...rest] = part.trim().split('=');
    if (name === SESSION_COOKIE_NAME) {
      return rest.join('=') || null;
    }
  }

  return null;
}

/**
 * Builds the `Set-Cookie` header value for a new, valid session.
 *
 * `SameSite=None` (not `Strict`) is required here: the dashboard
 * (`*.pages.dev`) and this Worker (`*.workers.dev`) are two different
 * registrable domains -- genuinely cross-site, not just cross-port like the
 * local `wrangler dev` setup. `SameSite=Strict`/`Lax` cookies are never sent
 * on a cross-site `fetch`, even with `credentials: 'include'`, which made
 * the dashboard's own login appear to silently fail (confirmed against the
 * real deployment: the login POST itself succeeded, but the browser never
 * sent the cookie back on the next request). `SameSite=None` requires
 * `Secure`, which was already set.
 */
export function buildSessionCookie(sessionId, expiresAt) {
  const expires = new Date(expiresAt).toUTCString();
  return `${SESSION_COOKIE_NAME}=${sessionId}; Path=/; Expires=${expires}; HttpOnly; Secure; SameSite=None`;
}

/** Builds the `Set-Cookie` header value that clears the session cookie. */
export function buildExpiredSessionCookie() {
  return `${SESSION_COOKIE_NAME}=; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT; HttpOnly; Secure; SameSite=None`;
}
