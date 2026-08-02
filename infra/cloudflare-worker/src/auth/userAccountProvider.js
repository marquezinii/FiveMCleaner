import { hashIp, hashPassword, verifyPassword, generateSessionId, hashSessionId } from './crypto.js';
import { isLockedOut, nextStateAfterFailure, stateAfterSuccess } from './bruteForceGuard.js';
import { readBoundedJson } from '../requestSecurity.js';

const MAX_BODY_BYTES = 4 * 1024;
const SESSION_LIFETIME_MS = 30 * 24 * 60 * 60 * 1000;
const REGISTRATION_WINDOW_MS = 60 * 60 * 1000;
const MAX_REGISTRATIONS_PER_WINDOW = 5;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const USERNAME = /^[a-z0-9](?:[a-z0-9._]{1,28}[a-z0-9])$/;
const PERSON_NAME = /^[\p{L}\p{M}][\p{L}\p{M}' -]*$/u;
export const CURRENT_TERMS_VERSION = '2026-08-02';

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' },
  });
}

function profile(row) {
  return { firstName: row.first_name, lastName: row.last_name, username: row.username_normalized, email: row.email_normalized };
}

export function normalizeRegistration(body) {
  const firstName = typeof body?.firstName === 'string' ? body.firstName.trim() : '';
  const lastName = typeof body?.lastName === 'string' ? body.lastName.trim() : '';
  const username = typeof body?.username === 'string' ? body.username.trim().toLowerCase() : '';
  const email = typeof body?.email === 'string' ? body.email.trim().toLowerCase() : '';
  const password = body?.password;
  if (!firstName || firstName.length > 80 || !PERSON_NAME.test(firstName)
    || !lastName || lastName.length > 80 || !PERSON_NAME.test(lastName)
    || !USERNAME.test(username)
    || !EMAIL.test(email) || email.length > 254 || typeof password !== 'string'
    || password.length < 10 || password.length > 1024
    || body?.termsAccepted !== true || body?.termsVersion !== CURRENT_TERMS_VERSION) return null;
  return { firstName, lastName, username, email, password };
}

async function accountConflict(db, input) {
  const row = await db.prepare(`SELECT email_normalized, username_normalized FROM user_accounts
    WHERE email_normalized = ? OR username_normalized = ? LIMIT 1`).bind(input.email, input.username).first();
  if (!row) return null;
  return row.email_normalized === input.email ? 'email-in-use' : 'username-in-use';
}

async function reserveRegistration(db, ipHash, timestamp) {
  const windowCutoff = new Date(timestamp.getTime() - REGISTRATION_WINDOW_MS).toISOString();
  const reservation = await db.prepare(`INSERT INTO user_registration_attempts (ip_hash, account_count, window_started_at)
    VALUES (?, 1, ?) ON CONFLICT(ip_hash) DO UPDATE SET
    account_count = CASE WHEN window_started_at <= ? THEN 1 ELSE account_count + 1 END,
    window_started_at = CASE WHEN window_started_at <= ? THEN excluded.window_started_at ELSE window_started_at END
    WHERE window_started_at <= ? OR account_count < ? RETURNING account_count`)
    .bind(ipHash, timestamp.toISOString(), windowCutoff, windowCutoff, windowCutoff, MAX_REGISTRATIONS_PER_WINDOW).first();
  return reservation !== null;
}

async function attempt(db, ipHash) {
  return db.prepare('SELECT failed_count, first_failed_at, locked_until FROM user_login_attempts WHERE ip_hash = ?').bind(ipHash).first();
}

async function saveAttempt(db, ipHash, row) {
  if (row === null) return db.prepare('DELETE FROM user_login_attempts WHERE ip_hash = ?').bind(ipHash).run();
  return db.prepare(`INSERT INTO user_login_attempts (ip_hash, failed_count, first_failed_at, locked_until)
    VALUES (?, ?, ?, ?) ON CONFLICT(ip_hash) DO UPDATE SET failed_count = excluded.failed_count,
    first_failed_at = excluded.first_failed_at, locked_until = excluded.locked_until`)
    .bind(ipHash, row.failed_count, row.first_failed_at, row.locked_until).run();
}

async function createSession(db, user, now) {
  const token = generateSessionId();
  const id = await hashSessionId(token);
  const expiresAt = new Date(now.getTime() + SESSION_LIFETIME_MS).toISOString();
  await db.prepare('INSERT INTO user_sessions (id, user_id, created_at, expires_at, revoked_at) VALUES (?, ?, ?, ?, ?)')
    .bind(id, user.id, now.toISOString(), expiresAt, null).run();
  return { token, profile: profile(user) };
}

export function createUserAccountProvider(env, now = () => new Date()) {
  const db = env.TELEMETRY_DB;
  const ipHashFor = (request) => hashIp(request.headers.get('CF-Connecting-IP') ?? 'unknown', env.IP_HASH_SECRET);
  return {
    async register(request) {
      const input = normalizeRegistration(await readBoundedJson(request, MAX_BODY_BYTES));
      if (!input) return json({ error: 'invalid-request' }, 400);
      const conflict = await accountConflict(db, input);
      if (conflict) return json({ error: conflict }, 409);
      const timestamp = now();
      const ipHash = await ipHashFor(request);
      if (!await reserveRegistration(db, ipHash, timestamp)) {
        return json({ error: 'too-many-attempts' }, 429);
      }
      const user = {
        id: crypto.randomUUID(),
        first_name: input.firstName,
        last_name: input.lastName,
        username_normalized: input.username,
        email_normalized: input.email,
      };
      try {
        await db.prepare(`INSERT INTO user_accounts
          (id, first_name, last_name, username_normalized, email_normalized, password_hash, created_at, terms_version, terms_accepted_at)
          VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`)
          .bind(user.id, user.first_name, user.last_name, user.username_normalized, user.email_normalized,
            await hashPassword(input.password), timestamp.toISOString(), CURRENT_TERMS_VERSION, timestamp.toISOString()).run();
      } catch (error) {
        const concurrentConflict = await accountConflict(db, input);
        if (concurrentConflict) return json({ error: concurrentConflict }, 409);
        throw error;
      }
      return json(await createSession(db, user, timestamp), 201);
    },
    async login(request) {
      const body = await readBoundedJson(request, MAX_BODY_BYTES);
      const email = typeof body?.email === 'string' ? body.email.trim().toLowerCase() : '';
      const password = body?.password;
      if (!EMAIL.test(email) || typeof password !== 'string' || password.length > 1024) return json({ error: 'invalid-request' }, 400);
      const ipHash = await ipHashFor(request);
      const timestamp = now();
      const previous = await attempt(db, ipHash);
      if (isLockedOut(previous, timestamp)) return json({ error: 'too-many-attempts' }, 429);
      const user = await db.prepare('SELECT id, first_name, last_name, username_normalized, email_normalized, password_hash FROM user_accounts WHERE email_normalized = ?').bind(email).first();
      if (!user || !(await verifyPassword(password, user.password_hash))) {
        await saveAttempt(db, ipHash, nextStateAfterFailure(previous, timestamp));
        return json({ error: 'invalid-credentials' }, 401);
      }
      await saveAttempt(db, ipHash, stateAfterSuccess());
      return json(await createSession(db, user, timestamp));
    },
    async session(request) {
      const token = request.headers.get('Authorization')?.replace(/^Bearer\s+/, '');
      if (!token) return json({ error: 'unauthorized' }, 401);
      const user = await db.prepare(`SELECT a.first_name, a.last_name, a.username_normalized, a.email_normalized
        FROM user_sessions s JOIN user_accounts a ON a.id = s.user_id
        WHERE s.id = ? AND s.revoked_at IS NULL AND s.expires_at > ?`)
        .bind(await hashSessionId(token), now().toISOString()).first();
      return user ? json({ profile: profile(user) }) : json({ error: 'unauthorized' }, 401);
    },
    async logout(request) {
      const token = request.headers.get('Authorization')?.replace(/^Bearer\s+/, '');
      if (token) await db.prepare('UPDATE user_sessions SET revoked_at = ? WHERE id = ?')
        .bind(now().toISOString(), await hashSessionId(token)).run();
      return json({ success: true });
    },
  };
}
