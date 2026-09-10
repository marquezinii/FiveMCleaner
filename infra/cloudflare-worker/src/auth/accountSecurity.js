const encoder = new TextEncoder();
const RECOVERY_ALPHABET = '23456789ABCDEFGHJKLMNPQRSTUVWXYZ';
const RECOVERY_COUNT = 10;
const RECOVERY_RESERVATION_SECONDS = 120;
const ENROLLMENT_PATTERN = /^[A-Za-z0-9_-]{1,256}$/;
const RECOVERY_CODE_PATTERN = /^[23456789A-HJ-NP-Z]{5}-?[23456789A-HJ-NP-Z]{5}$/;

async function hmac(secret, value) {
  if (typeof secret !== 'string' || secret.length < 32) throw new Error('recovery-secret-misconfigured');
  const key = await crypto.subtle.importKey(
    'raw', encoder.encode(secret), { name: 'HMAC', hash: 'SHA-256' }, false, ['sign'],
  );
  const signature = new Uint8Array(await crypto.subtle.sign('HMAC', key, encoder.encode(value)));
  return [...signature].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

export function isRecentAuthentication(auth, nowSeconds = Math.floor(Date.now() / 1000), maxAgeSeconds = 300) {
  return Number.isFinite(auth?.authTime)
    && auth.authTime <= nowSeconds + 60
    && auth.authTime >= nowSeconds - maxAgeSeconds;
}

export function validateEnrollmentId(value) {
  return typeof value === 'string' && ENROLLMENT_PATTERN.test(value) ? value : null;
}

export function validateRecoveryRequest(payload) {
  if (!payload || typeof payload !== 'object' || Array.isArray(payload)) return null;
  if (Object.keys(payload).length !== 3) return null;
  const { mfaPendingCredential, mfaEnrollmentId, recoveryCode } = payload;
  if (typeof mfaPendingCredential !== 'string' || mfaPendingCredential.length < 16
    || mfaPendingCredential.length > 4096 || !ENROLLMENT_PATTERN.test(mfaEnrollmentId)
    || typeof recoveryCode !== 'string') return null;
  const normalizedCode = recoveryCode.trim().toUpperCase();
  if (!RECOVERY_CODE_PATTERN.test(normalizedCode)) return null;
  return {
    mfaPendingCredential,
    mfaEnrollmentId,
    recoveryCode: normalizedCode.replace('-', ''),
  };
}

export function generateRecoveryCodes(randomBytes = crypto.getRandomValues(new Uint8Array(RECOVERY_COUNT * 10))) {
  if (!(randomBytes instanceof Uint8Array) || randomBytes.length !== RECOVERY_COUNT * 10) {
    throw new Error('invalid-random-source');
  }
  const codes = [];
  for (let offset = 0; offset < randomBytes.length; offset += 10) {
    let raw = '';
    for (let index = 0; index < 10; index += 1) raw += RECOVERY_ALPHABET[randomBytes[offset + index] & 31];
    codes.push(`${raw.slice(0, 5)}-${raw.slice(5)}`);
  }
  return codes;
}

export async function recoveryRateLimitKey(secret, enrollmentId, callerKey) {
  return `account-recovery:${await hmac(secret, `enrollment\0${enrollmentId}\0caller\0${callerKey}`)}`;
}

async function recoveryHashes(secret, enrollmentId, code) {
  const enrollmentTag = await hmac(secret, `enrollment\0${enrollmentId}`);
  return {
    enrollmentTag,
    codeHash: await hmac(secret, `code\0${enrollmentTag}\0${code.replace('-', '')}`),
  };
}

export async function replaceRecoveryCodes(db, uid, enrollmentId, secret, codes, now = new Date()) {
  const generationId = crypto.randomUUID();
  const statements = [db.prepare('DELETE FROM account_mfa_recovery_codes WHERE account_uid = ?').bind(uid)];
  for (const code of codes) {
    const { enrollmentTag, codeHash } = await recoveryHashes(secret, enrollmentId, code);
    statements.push(db.prepare(
      `INSERT INTO account_mfa_recovery_codes
         (account_uid, enrollment_tag, code_hash, generation_id, created_at)
       VALUES (?, ?, ?, ?, ?)`,
    ).bind(uid, enrollmentTag, codeHash, generationId, now.toISOString()));
  }
  await db.batch(statements);
}

export async function deleteRecoveryCodes(db, uid, enrollmentId, secret) {
  const enrollmentTag = await hmac(secret, `enrollment\0${enrollmentId}`);
  await db.prepare(
    'DELETE FROM account_mfa_recovery_codes WHERE account_uid = ? AND enrollment_tag = ?',
  ).bind(uid, enrollmentTag).run();
}

export async function findRecoveryCode(db, enrollmentId, code, secret) {
  const { enrollmentTag, codeHash } = await recoveryHashes(secret, enrollmentId, code);
  const row = await db.prepare(
    `SELECT id, account_uid, reserved_until FROM account_mfa_recovery_codes
     WHERE enrollment_tag = ? AND code_hash = ? AND used_at IS NULL`,
  ).bind(enrollmentTag, codeHash).first();
  return row ? { id: row.id, uid: row.account_uid, reservedUntil: row.reserved_until } : null;
}

export async function reserveRecoveryCode(db, id, now = new Date()) {
  const nowIso = now.toISOString();
  const reservedUntil = new Date(now.getTime() + RECOVERY_RESERVATION_SECONDS * 1000).toISOString();
  const result = await db.prepare(
    `UPDATE account_mfa_recovery_codes SET reserved_until = ?
     WHERE id = ? AND used_at IS NULL AND (reserved_until IS NULL OR reserved_until < ?)`,
  ).bind(reservedUntil, id, nowIso).run();
  return Number(result?.meta?.changes ?? result?.changes ?? 0) === 1;
}

export async function releaseRecoveryCode(db, id) {
  await db.prepare(
    'UPDATE account_mfa_recovery_codes SET reserved_until = NULL WHERE id = ? AND used_at IS NULL',
  ).bind(id).run();
}

export async function completeRecovery(db, id, uid, validAfter, now = new Date()) {
  await db.batch([
    db.prepare(
      `INSERT INTO account_auth_cutoffs (account_uid, valid_after, updated_at) VALUES (?, ?, ?)
       ON CONFLICT(account_uid) DO UPDATE SET valid_after = excluded.valid_after, updated_at = excluded.updated_at`,
    ).bind(uid, validAfter, now.toISOString()),
    db.prepare(
      `UPDATE account_mfa_recovery_codes SET used_at = ?, reserved_until = NULL
       WHERE id = ? AND account_uid = ? AND used_at IS NULL`,
    ).bind(now.toISOString(), id, uid),
  ]);
}

export async function tokenPassesAccountCutoff(db, auth) {
  const row = await db.prepare('SELECT valid_after FROM account_auth_cutoffs WHERE account_uid = ?')
    .bind(auth.uid).first();
  return row === null || (Number.isFinite(auth.authTime) && auth.authTime > Number(row.valid_after));
}
