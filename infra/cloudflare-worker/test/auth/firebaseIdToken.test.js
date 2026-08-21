import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import {
  FIREBASE_ID_TOKEN_ISSUER,
  FIREBASE_JWKS_URL,
  FIREBASE_PROJECT_ID,
  clearFirebaseJwksCache,
  requireFirebaseUser,
  verifyFirebaseIdToken,
} from '../../src/auth/firebaseIdToken.js';

function toBase64Url(bytes) {
  let binary = '';
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
}

function encodeJson(value) {
  return toBase64Url(new TextEncoder().encode(JSON.stringify(value)));
}

async function generateRsaKeyPair() {
  return crypto.subtle.generateKey(
    {
      name: 'RSASSA-PKCS1-v1_5',
      modulusLength: 2048,
      publicExponent: new Uint8Array([1, 0, 1]),
      hash: 'SHA-256',
    },
    true,
    ['sign', 'verify'],
  );
}

async function publicJwk(publicKey, kid) {
  const jwk = await crypto.subtle.exportKey('jwk', publicKey);
  return {
    kty: 'RSA',
    kid,
    n: jwk.n,
    e: jwk.e,
    alg: 'RS256',
    use: 'sig',
  };
}

async function signToken(privateKey, header, payload) {
  const encodedHeader = encodeJson(header);
  const encodedPayload = encodeJson(payload);
  const data = new TextEncoder().encode(`${encodedHeader}.${encodedPayload}`);
  const signature = new Uint8Array(
    await crypto.subtle.sign({ name: 'RSASSA-PKCS1-v1_5' }, privateKey, data),
  );
  return `${encodedHeader}.${encodedPayload}.${toBase64Url(signature)}`;
}

function validPayload(overrides = {}) {
  const now = Math.floor(Date.now() / 1000);
  return {
    aud: FIREBASE_PROJECT_ID,
    iss: FIREBASE_ID_TOKEN_ISSUER,
    sub: 'firebase-uid-abc123',
    iat: now - 30,
    exp: now + 3600,
    auth_time: now - 30,
    ...overrides,
  };
}

function mockJwksFetch(keysByKid, { status = 200, maxAge = 3600, onCall } = {}) {
  return async (url) => {
    onCall?.(url);
    assert.equal(url, FIREBASE_JWKS_URL);
    return {
      ok: status >= 200 && status < 300,
      status,
      headers: {
        get(name) {
          if (String(name).toLowerCase() === 'cache-control') {
            return `public, max-age=${maxAge}`;
          }

          return null;
        },
      },
      async json() {
        return { keys: Object.values(keysByKid) };
      },
    };
  };
}

beforeEach(() => {
  clearFirebaseJwksCache();
});

test('verifyFirebaseIdToken accepts a valid RS256 Firebase-shaped token', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'test-kid-1';
  const jwk = await publicJwk(publicKey, kid);
  const token = await signToken(privateKey, { alg: 'RS256', kid }, validPayload());

  const result = await verifyFirebaseIdToken(token, {
    fetch: mockJwksFetch({ [kid]: jwk }),
  });

  assert.equal(result.uid, 'firebase-uid-abc123');
  assert.equal(result.payload.sub, 'firebase-uid-abc123');
});

test('verifyFirebaseIdToken rejects a bad signature', async () => {
  const good = await generateRsaKeyPair();
  const other = await generateRsaKeyPair();
  const kid = 'test-kid-2';
  const jwk = await publicJwk(good.publicKey, kid);
  const token = await signToken(other.privateKey, { alg: 'RS256', kid }, validPayload());

  await assert.rejects(
    () =>
      verifyFirebaseIdToken(token, {
        fetch: mockJwksFetch({ [kid]: jwk }),
      }),
    /bad-signature/,
  );
});

test('verifyFirebaseIdToken rejects wrong audience', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'test-kid-aud';
  const jwk = await publicJwk(publicKey, kid);
  const token = await signToken(
    privateKey,
    { alg: 'RS256', kid },
    validPayload({ aud: 'other-project' }),
  );

  await assert.rejects(
    () =>
      verifyFirebaseIdToken(token, {
        fetch: mockJwksFetch({ [kid]: jwk }),
      }),
    /bad-aud/,
  );
});

test('verifyFirebaseIdToken rejects wrong issuer', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'test-kid-iss';
  const jwk = await publicJwk(publicKey, kid);
  const token = await signToken(
    privateKey,
    { alg: 'RS256', kid },
    validPayload({ iss: 'https://securetoken.google.com/other-project' }),
  );

  await assert.rejects(
    () =>
      verifyFirebaseIdToken(token, {
        fetch: mockJwksFetch({ [kid]: jwk }),
      }),
    /bad-iss/,
  );
});

test('verifyFirebaseIdToken rejects expired tokens outside skew', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'test-kid-exp';
  const jwk = await publicJwk(publicKey, kid);
  const nowMs = Date.UTC(2026, 7, 4, 12, 0, 0);
  const nowSec = Math.floor(nowMs / 1000);
  const token = await signToken(
    privateKey,
    { alg: 'RS256', kid },
    validPayload({ iat: nowSec - 7200, exp: nowSec - 120 }),
  );

  await assert.rejects(
    () =>
      verifyFirebaseIdToken(token, {
        fetch: mockJwksFetch({ [kid]: jwk }),
        nowMs,
        clockSkewSeconds: 60,
      }),
    /expired/,
  );
});

test('verifyFirebaseIdToken rejects non-RS256 headers', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'test-kid-alg';
  const jwk = await publicJwk(publicKey, kid);
  const token = await signToken(privateKey, { alg: 'none', kid }, validPayload());

  await assert.rejects(
    () =>
      verifyFirebaseIdToken(token, {
        fetch: mockJwksFetch({ [kid]: jwk }),
      }),
    /invalid-alg/,
  );
});

test('verifyFirebaseIdToken refreshes JWKS when kid is unknown in cache', async () => {
  const first = await generateRsaKeyPair();
  const second = await generateRsaKeyPair();
  const kidA = 'kid-a';
  const kidB = 'kid-b';
  const jwkA = await publicJwk(first.publicKey, kidA);
  const jwkB = await publicJwk(second.publicKey, kidB);
  const baseTime = Date.now();

  let calls = 0;
  const fetchImpl = async (url) => {
    calls += 1;
    assert.equal(url, FIREBASE_JWKS_URL);
    const keys = calls === 1 ? { [kidA]: jwkA } : { [kidA]: jwkA, [kidB]: jwkB };
    return mockJwksFetch(keys)(url);
  };

  const tokenA = await signToken(first.privateKey, { alg: 'RS256', kid: kidA }, validPayload());
  await verifyFirebaseIdToken(tokenA, { fetch: fetchImpl, nowMs: baseTime });

  const tokenB = await signToken(
    second.privateKey,
    { alg: 'RS256', kid: kidB },
    validPayload({ sub: 'uid-b' }),
  );
  const result = await verifyFirebaseIdToken(tokenB, {
    fetch: fetchImpl,
    nowMs: baseTime + 31_000,
  });

  assert.equal(result.uid, 'uid-b');
  assert.equal(calls, 2);
});

test('verifyFirebaseIdToken rate-limits and negatively caches unknown kid refreshes', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const knownKid = 'known-kid';
  const jwk = await publicJwk(publicKey, knownKid);
  let calls = 0;
  const fetchImpl = mockJwksFetch(
    { [knownKid]: jwk },
    { onCall() { calls += 1; } },
  );
  const baseTime = Date.now();
  const knownToken = await signToken(privateKey, { alg: 'RS256', kid: knownKid }, validPayload());
  await verifyFirebaseIdToken(knownToken, { fetch: fetchImpl, nowMs: baseTime });

  const unknownToken = await signToken(privateKey, { alg: 'RS256', kid: 'unknown-a' }, validPayload());
  await assert.rejects(
    () => verifyFirebaseIdToken(unknownToken, { fetch: fetchImpl, nowMs: baseTime + 31_000 }),
    /unknown-kid/,
  );
  const otherUnknownToken = await signToken(
    privateKey,
    { alg: 'RS256', kid: 'unknown-b' },
    validPayload(),
  );
  await assert.rejects(
    () => verifyFirebaseIdToken(otherUnknownToken, { fetch: fetchImpl, nowMs: baseTime + 32_000 }),
    /unknown-kid/,
  );
  await assert.rejects(
    () => verifyFirebaseIdToken(unknownToken, { fetch: fetchImpl, nowMs: baseTime + 61_000 }),
    /unknown-kid/,
  );

  assert.equal(calls, 2);
});

test('verifyFirebaseIdToken coalesces concurrent JWKS fetches', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'coalesced-kid';
  const jwk = await publicJwk(publicKey, kid);
  let calls = 0;
  const fetchImpl = async (url) => {
    calls += 1;
    await new Promise((resolve) => setTimeout(resolve, 10));
    return mockJwksFetch({ [kid]: jwk })(url);
  };
  const token = await signToken(privateKey, { alg: 'RS256', kid }, validPayload());

  const results = await Promise.all([
    verifyFirebaseIdToken(token, { fetch: fetchImpl }),
    verifyFirebaseIdToken(token, { fetch: fetchImpl }),
    verifyFirebaseIdToken(token, { fetch: fetchImpl }),
  ]);

  assert.equal(results.length, 3);
  assert.equal(calls, 1);
});

test('verifyFirebaseIdToken reuses cached JWKS for the same kid', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'kid-cache';
  const jwk = await publicJwk(publicKey, kid);
  let calls = 0;
  const fetchImpl = mockJwksFetch(
    { [kid]: jwk },
    {
      onCall() {
        calls += 1;
      },
    },
  );

  const nowMs = Date.now();
  const token = await signToken(privateKey, { alg: 'RS256', kid }, validPayload());
  await verifyFirebaseIdToken(token, { fetch: fetchImpl, nowMs });
  await verifyFirebaseIdToken(token, { fetch: fetchImpl, nowMs: nowMs + 10_000 });

  assert.equal(calls, 1);
});

test('requireFirebaseUser returns uid for a valid Bearer token', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'kid-bearer';
  const jwk = await publicJwk(publicKey, kid);
  const token = await signToken(privateKey, { alg: 'RS256', kid }, validPayload());

  const result = await requireFirebaseUser(
    new Request('https://worker.example/account/me', {
      headers: { Authorization: `Bearer ${token}` },
    }),
    { fetch: mockJwksFetch({ [kid]: jwk }) },
  );

  assert.equal(result.authorized, true);
  assert.equal(result.uid, 'firebase-uid-abc123');
  assert.equal(result.emailVerified, false);
});

test('requireFirebaseUser exposes a verified e-mail claim only when Firebase asserted it', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'kid-verified';
  const token = await signToken(privateKey, { alg: 'RS256', kid }, validPayload({ email_verified: true }));
  const result = await requireFirebaseUser(
    new Request('https://worker.example/account/me', { headers: { Authorization: `Bearer ${token}` } }),
    { fetch: mockJwksFetch({ [kid]: await publicJwk(publicKey, kid) }) },
  );

  assert.equal(result.authorized, true);
  assert.equal(result.emailVerified, true);
});

test('requireFirebaseUser returns generic 401 without Authorization', async () => {
  const result = await requireFirebaseUser(new Request('https://worker.example/account/me'));

  assert.equal(result.authorized, false);
  assert.equal(result.response.status, 401);
  assert.deepEqual(await result.response.json(), { error: 'unauthorized' });
});

test('requireFirebaseUser returns generic 401 for invalid tokens', async () => {
  const { privateKey, publicKey } = await generateRsaKeyPair();
  const kid = 'kid-bad';
  const jwk = await publicJwk(publicKey, kid);
  const token = await signToken(
    privateKey,
    { alg: 'RS256', kid },
    validPayload({ aud: 'nope' }),
  );

  const result = await requireFirebaseUser(
    new Request('https://worker.example/account/me', {
      headers: { Authorization: `Bearer ${token}` },
    }),
    { fetch: mockJwksFetch({ [kid]: jwk }) },
  );

  assert.equal(result.authorized, false);
  assert.equal(result.response.status, 401);
  assert.deepEqual(await result.response.json(), { error: 'unauthorized' });
});

test('requireFirebaseUser rejects malformed Authorization headers', async () => {
  const cases = ['Bearer', 'Bearer ', 'Basic abc', 'bearer', 'Token abc'];

  for (const value of cases) {
    const result = await requireFirebaseUser(
      new Request('https://worker.example/account/me', {
        headers: { Authorization: value },
      }),
    );
    assert.equal(result.authorized, false, value);
    assert.equal(result.response.status, 401, value);
  }
});
