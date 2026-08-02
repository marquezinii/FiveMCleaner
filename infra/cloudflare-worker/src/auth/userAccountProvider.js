import { hashIp, hashPassword, verifyPassword, generateSessionId } from './crypto.js';
import { isLockedOut, nextStateAfterFailure, stateAfterSuccess } from './bruteForceGuard.js';
import { readBoundedJson } from '../requestSecurity.js';

const MAX_BODY_BYTES = 4 * 1024;
const SESSION_LIFETIME_MS = 30 * 24 * 60 * 60 * 1000;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function profile(row) {
  return { firstName: row.first_name, lastName: row.last_name, email: row.email_normalized };
}

function normalizeRegistration(body) {
  const firstName = typeof body?.firstName === 'string' ? body.firstName.trim() : '';
  const lastName = typeof body?.lastName === 'string' ? body.lastName.trim() : '';
  const email = typeof body?.email === 'string' ? body.email.trim().toLowerCase() : '';
  const password = body?.password;
  if (!firstName || firstName.length > 80 || !lastName || lastName.length > 80
    || !EMAIL.test(email) || email.length > 254 || typeof password !== 'string'
    || password.length < 8 || password.length > 1024) return null;
  return { firstName, lastName, email, password };
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
  const id = generateSessionId();
  const expiresAt = new Date(now.getTime() + SESSION_LIFETIME_MS).toISOString();
  await db.prepare('INSERT INTO user_sessions (id, user_id, created_at, expires_at, revoked_at) VALUES (?, ?, ?, ?, ?)')
    .bind(id, user.id, now.toISOString(), expiresAt, null).run();
  return { token: id, profile: profile(user) };
}

export function createUserAccountProvider(env, now = () => new Date()) {
  const db = env.TELEMETRY_DB;
  const ipHashFor = (request) => hashIp(request.headers.get('CF-Connecting-IP') ?? 'unknown', env.IP_HASH_SECRET);
  return {
    async register(request) {
      const input = normalizeRegistration(await readBoundedJson(request, MAX_BODY_BYTES));
      if (!input) return json({ error: 'invalid-request' }, 400);
      const existing = await db.prepare('SELECT id FROM user_accounts WHERE email_normalized = ?').bind(input.email).first();
      if (existing) return json({ error: 'email-in-use' }, 409);
      const timestamp = now();
      const user = { id: crypto.randomUUID(), first_name: input.firstName, last_name: input.lastName, email_normalized: input.email };
      await db.prepare(`INSERT INTO user_accounts (id, first_name, last_name, email_normalized, password_hash, created_at)
        VALUES (?, ?, ?, ?, ?, ?)`)
        .bind(user.id, user.first_name, user.last_name, user.email_normalized, await hashPassword(input.password), timestamp.toISOString()).run();
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
      const user = await db.prepare('SELECT id, first_name, last_name, email_normalized, password_hash FROM user_accounts WHERE email_normalized = ?').bind(email).first();
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
      const user = await db.prepare(`SELECT a.first_name, a.last_name, a.email_normalized
        FROM user_sessions s JOIN user_accounts a ON a.id = s.user_id
        WHERE s.id = ? AND s.revoked_at IS NULL AND s.expires_at > ?`).bind(token, now().toISOString()).first();
      return user ? json({ profile: profile(user) }) : json({ error: 'unauthorized' }, 401);
    },
    async logout(request) {
      const token = request.headers.get('Authorization')?.replace(/^Bearer\s+/, '');
      if (token) await db.prepare('UPDATE user_sessions SET revoked_at = ? WHERE id = ?').bind(now().toISOString(), token).run();
      return json({ success: true });
    },
  };
}
