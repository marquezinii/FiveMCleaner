import { FIREBASE_PROJECT_ID, verifyFirebaseIdToken } from './firebaseIdToken.js';

const OAUTH_TOKEN_URL = 'https://oauth2.googleapis.com/token';
const IDENTITY_TOOLKIT_URL = 'https://identitytoolkit.googleapis.com';
const CLOUD_PLATFORM_SCOPE = 'https://www.googleapis.com/auth/cloud-platform';
const encoder = new TextEncoder();
let cachedAccessToken = null;

function base64Url(bytes) {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

function encodeJson(value) {
  return base64Url(encoder.encode(JSON.stringify(value)));
}

function pemBytes(value) {
  const normalized = value.replaceAll('\\n', '\n');
  const body = normalized
    .replace('-----BEGIN PRIVATE KEY-----', '')
    .replace('-----END PRIVATE KEY-----', '')
    .replaceAll(/\s/g, '');
  if (!body) throw new Error('firebase-admin-misconfigured');
  return Uint8Array.from(atob(body), (character) => character.charCodeAt(0));
}

async function serviceAccountAssertion(clientEmail, privateKey, nowSeconds) {
  const header = encodeJson({ alg: 'RS256', typ: 'JWT' });
  const payload = encodeJson({
    iss: clientEmail,
    scope: CLOUD_PLATFORM_SCOPE,
    aud: OAUTH_TOKEN_URL,
    iat: nowSeconds,
    exp: nowSeconds + 3600,
  });
  const key = await crypto.subtle.importKey(
    'pkcs8',
    pemBytes(privateKey),
    { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' },
    false,
    ['sign'],
  );
  const unsigned = `${header}.${payload}`;
  const signature = await crypto.subtle.sign('RSASSA-PKCS1-v1_5', key, encoder.encode(unsigned));
  return `${unsigned}.${base64Url(new Uint8Array(signature))}`;
}

async function accessToken(env, options = {}) {
  const nowSeconds = Math.floor((options.nowMs ?? Date.now()) / 1000);
  if (cachedAccessToken?.expiresAt > nowSeconds + 60) return cachedAccessToken.value;
  if (!env.FIREBASE_ADMIN_CLIENT_EMAIL || !env.FIREBASE_ADMIN_PRIVATE_KEY) {
    throw new Error('firebase-admin-misconfigured');
  }
  const assertion = await serviceAccountAssertion(
    env.FIREBASE_ADMIN_CLIENT_EMAIL,
    env.FIREBASE_ADMIN_PRIVATE_KEY,
    nowSeconds,
  );
  const fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
  const response = await fetchImpl(OAUTH_TOKEN_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'urn:ietf:params:oauth:grant-type:jwt-bearer',
      assertion,
    }),
  });
  const body = await response.json().catch(() => null);
  if (!response.ok || typeof body?.access_token !== 'string' || body.access_token.length > 4096) {
    throw new Error('firebase-admin-auth-failed');
  }
  const lifetime = Number.isFinite(Number(body.expires_in)) ? Math.min(Number(body.expires_in), 3600) : 3600;
  cachedAccessToken = { value: body.access_token, expiresAt: nowSeconds + lifetime };
  return body.access_token;
}

async function adminRequest(env, operation, body, options = {}) {
  const token = await accessToken(env, options);
  const projectId = env.FIREBASE_PROJECT_ID || FIREBASE_PROJECT_ID;
  const fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
  const response = await fetchImpl(
    `${IDENTITY_TOOLKIT_URL}/v1/projects/${encodeURIComponent(projectId)}/accounts:${operation}`,
    {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    },
  );
  const result = await response.json().catch(() => null);
  const errorCode = result?.error?.message?.split(' : ', 1)[0];
  if (!response.ok && !(operation === 'delete' && errorCode === 'USER_NOT_FOUND')) {
    throw new Error(`firebase-admin-${operation}-failed`);
  }
  return result;
}

export async function accountHasTotpEnrollment(env, uid, enrollmentId, options = {}) {
  const result = await adminRequest(env, 'lookup', { localId: [uid] }, options);
  const user = Array.isArray(result?.users) && result.users.length === 1 ? result.users[0] : null;
  return Array.isArray(user?.mfaInfo)
    && user.mfaInfo.some((entry) => entry?.mfaEnrollmentId === enrollmentId && entry?.totpInfo);
}

export async function accountSessionIsCurrent(env, uid, issuedAt, options = {}) {
  if (!Number.isFinite(issuedAt)) return false;
  const result = await adminRequest(env, 'lookup', { localId: [uid] }, options);
  const user = Array.isArray(result?.users) && result.users.length === 1 ? result.users[0] : null;
  if (user === null || user.disabled === true) return false;
  const validSince = Number(user.validSince);
  return !Number.isFinite(validSince) || issuedAt >= validSince;
}

export async function removeMfaAndRevokeSessions(env, uid, validSince, options = {}) {
  await adminRequest(env, 'update', {
    localId: uid,
    mfa: { enrollments: [] },
    validSince: String(validSince),
  }, options);
}

export async function deleteFirebaseAccount(env, uid, options = {}) {
  await adminRequest(env, 'delete', { localId: uid }, options);
}

export async function provePendingTotpEnrollment(env, pendingCredential, enrollmentId, options = {}) {
  if (!env.FIREBASE_WEB_API_KEY) throw new Error('firebase-web-api-misconfigured');
  const fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
  const response = await fetchImpl(
    `${IDENTITY_TOOLKIT_URL}/v2/accounts/mfaSignIn:finalize?key=${encodeURIComponent(env.FIREBASE_WEB_API_KEY)}`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        mfaPendingCredential: pendingCredential,
        mfaEnrollmentId: enrollmentId,
        totpVerificationInfo: { verificationCode: '000000' },
      }),
    },
  );
  const body = await response.json().catch(() => null);
  if (response.ok && typeof body?.idToken === 'string') {
    const verified = await verifyFirebaseIdToken(body.idToken, options.tokenVerification);
    return { valid: true, uid: verified.uid };
  }
  const errorCode = body?.error?.message?.split(' : ', 1)[0];
  return { valid: errorCode === 'INVALID_CODE' || errorCode === 'INVALID_VERIFICATION_CODE', uid: null };
}

export function clearFirebaseAdminTokenCache() {
  cachedAccessToken = null;
}
