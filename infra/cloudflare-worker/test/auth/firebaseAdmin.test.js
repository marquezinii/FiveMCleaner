import test from 'node:test';
import assert from 'node:assert/strict';
import {
  accountHasTotpEnrollment,
  accountSessionIsCurrent,
  clearFirebaseAdminTokenCache,
  deleteFirebaseAccount,
  provePendingTotpEnrollment,
  removeMfaAndRevokeSessions,
} from '../../src/auth/firebaseAdmin.js';
import { FIREBASE_ID_TOKEN_ISSUER, FIREBASE_PROJECT_ID } from '../../src/auth/firebaseIdToken.js';

function base64Url(bytes) {
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

async function firebaseToken(uid) {
  const pair = await crypto.subtle.generateKey({
    name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048,
    publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256',
  }, true, ['sign', 'verify']);
  const kid = 'probe-success';
  const encode = (value) => base64Url(new TextEncoder().encode(JSON.stringify(value)));
  const now = Math.floor(Date.now() / 1000);
  const unsigned = `${encode({ alg: 'RS256', kid })}.${encode({
    aud: FIREBASE_PROJECT_ID, iss: FIREBASE_ID_TOKEN_ISSUER, sub: uid, iat: now, exp: now + 60,
  })}`;
  const signature = await crypto.subtle.sign(
    'RSASSA-PKCS1-v1_5', pair.privateKey, new TextEncoder().encode(unsigned),
  );
  const jwk = await crypto.subtle.exportKey('jwk', pair.publicKey);
  return { token: `${unsigned}.${base64Url(new Uint8Array(signature))}`, kid, jwk };
}

async function adminEnv() {
  const pair = await crypto.subtle.generateKey({
    name: 'RSASSA-PKCS1-v1_5', modulusLength: 2048,
    publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256',
  }, true, ['sign', 'verify']);
  const bytes = new Uint8Array(await crypto.subtle.exportKey('pkcs8', pair.privateKey));
  let binary = '';
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return {
    FIREBASE_ADMIN_CLIENT_EMAIL: 'worker@example.test',
    FIREBASE_ADMIN_PRIVATE_KEY: `-----BEGIN PRIVATE KEY-----\n${btoa(binary)}\n-----END PRIVATE KEY-----`,
    FIREBASE_WEB_API_KEY: 'test-web-key',
  };
}

function jsonResponse(body, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function isOAuthTokenRequest(url) {
  const requestUrl = new URL(url);
  return requestUrl.protocol === 'https:'
    && requestUrl.hostname === 'oauth2.googleapis.com'
    && requestUrl.pathname === '/token';
}

test('admin lookup binds enrollment ownership to the requested uid', async () => {
  clearFirebaseAdminTokenCache();
  const env = await adminEnv();
  const requests = [];
  const fetch = async (url, init) => {
    requests.push({ url, init });
    if (isOAuthTokenRequest(url)) return jsonResponse({ access_token: 'access', expires_in: 3600 });
    return jsonResponse({ users: [{ localId: 'uid-1', mfaInfo: [{ mfaEnrollmentId: 'totp-1', totpInfo: {} }] }] });
  };
  assert.equal(await accountHasTotpEnrollment(env, 'uid-1', 'totp-1', { fetch }), true);
  assert.deepEqual(JSON.parse(requests[1].init.body), { localId: ['uid-1'] });
  assert.equal(requests[1].init.headers.Authorization, 'Bearer access');
});

test('admin session check rejects disabled, revoked, and missing accounts', async () => {
  clearFirebaseAdminTokenCache();
  const env = await adminEnv();
  const check = body => accountSessionIsCurrent(env, 'uid-1', 200, {
    fetch: async url => isOAuthTokenRequest(url)
      ? jsonResponse({ access_token: 'access', expires_in: 3600 })
      : jsonResponse(body),
  });

  assert.equal(await check({ users: [{ localId: 'uid-1', validSince: '200' }] }), true);
  assert.equal(await check({ users: [{ localId: 'uid-1', validSince: '201' }] }), false);
  assert.equal(await check({ users: [{ localId: 'uid-1', disabled: true }] }), false);
  assert.equal(await check({ users: [] }), false);
});

test('admin recovery removes all MFA enrollments and advances validSince without returning tokens', async () => {
  clearFirebaseAdminTokenCache();
  const env = await adminEnv();
  const requests = [];
  const fetch = async (url, init) => {
    requests.push({ url, init });
    return isOAuthTokenRequest(url)
      ? jsonResponse({ access_token: 'access', expires_in: 3600 })
      : jsonResponse({ idToken: 'must-not-be-returned', refreshToken: 'must-not-be-returned' });
  };
  assert.equal(await removeMfaAndRevokeSessions(env, 'uid-1', 1234, { fetch }), undefined);
  assert.deepEqual(JSON.parse(requests[1].init.body), {
    localId: 'uid-1', mfa: { enrollments: [] }, validSince: '1234',
  });
});

test('admin account deletion is scoped to the server-verified uid', async () => {
  clearFirebaseAdminTokenCache();
  const env = await adminEnv();
  const requests = [];
  const fetch = async (url, init) => {
    requests.push({ url, init });
    return isOAuthTokenRequest(url)
      ? jsonResponse({ access_token: 'access', expires_in: 3600 })
      : jsonResponse({});
  };
  await deleteFirebaseAccount(env, 'uid-1', { fetch });
  assert.match(requests[1].url, /accounts:delete$/);
  assert.deepEqual(JSON.parse(requests[1].init.body), { localId: 'uid-1' });
});

test('admin account deletion is idempotent when Firebase already removed the uid', async () => {
  clearFirebaseAdminTokenCache();
  const env = await adminEnv();
  const fetch = async url => isOAuthTokenRequest(url)
    ? jsonResponse({ access_token: 'access', expires_in: 3600 })
    : jsonResponse({ error: { message: 'USER_NOT_FOUND' } }, 400);

  await assert.doesNotReject(() => deleteFirebaseAccount(env, 'uid-gone', { fetch }));
});

test('pending proof accepts only Firebase invalid-code and never returns response tokens', async () => {
  const env = await adminEnv();
  const invalid = await provePendingTotpEnrollment(env, 'pending-credential', 'totp-1', {
    fetch: async () => jsonResponse({ error: { message: 'INVALID_VERIFICATION_CODE' } }, 400),
  });
  assert.deepEqual(invalid, { valid: true, uid: null });

  const canonicalInvalid = await provePendingTotpEnrollment(env, 'pending-credential', 'totp-1', {
    fetch: async () => jsonResponse({ error: { message: 'INVALID_CODE' } }, 400),
  });
  assert.deepEqual(canonicalInvalid, { valid: true, uid: null });

  const rejected = await provePendingTotpEnrollment(env, 'pending-credential', 'totp-2', {
    fetch: async () => jsonResponse({ error: { message: 'INVALID_MFA_PENDING_CREDENTIAL' } }, 400),
  });
  assert.deepEqual(rejected, { valid: false, uid: null });
});

test('an unexpectedly successful invalid probe returns only the verified uid', async () => {
  const env = await adminEnv();
  const signed = await firebaseToken('uid-from-probe');
  const result = await provePendingTotpEnrollment(env, 'pending-credential', 'totp-1', {
    fetch: async () => jsonResponse({ idToken: signed.token, refreshToken: 'must-never-escape' }),
    tokenVerification: {
      cacheHolder: { current: null, unknownKids: new Map() },
      fetch: async () => jsonResponse({ keys: [{
        ...signed.jwk, kid: signed.kid, alg: 'RS256', use: 'sig',
      }] }),
    },
  });
  assert.deepEqual(result, { valid: true, uid: 'uid-from-probe' });
  assert.equal('idToken' in result, false);
  assert.equal('refreshToken' in result, false);
});
