// Firebase Authentication ID token verification for future product routes.
// Pure Web Crypto (no npm crypto deps), same constraint as crypto.js.
//
// Callers must treat only `uid` (JWT `sub`) as the permanent internal user id.
// Never accept email as an identity claim for authorization.

export const FIREBASE_PROJECT_ID = 'fivemcleaner-app';
export const FIREBASE_ID_TOKEN_ISSUER = `https://securetoken.google.com/${FIREBASE_PROJECT_ID}`;
export const FIREBASE_JWKS_URL =
  'https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com';

const DEFAULT_CLOCK_SKEW_SECONDS = 60;
const DEFAULT_JWKS_TTL_MS = 60 * 60 * 1000;
const JWKS_REFRESH_COOLDOWN_MS = 30 * 1000;
const UNKNOWN_KID_TTL_MS = 60 * 1000;
const MAX_NEGATIVE_KIDS = 128;
const MAX_TOKEN_CHARS = 16 * 1024;

const textEncoder = new TextEncoder();

const defaultJwksState = {
  current: null,
  refreshPromise: null,
  lastRefreshAt: 0,
  unknownKids: new Map(),
};

export function clearFirebaseJwksCache() {
  defaultJwksState.current = null;
  defaultJwksState.refreshPromise = null;
  defaultJwksState.lastRefreshAt = 0;
  defaultJwksState.unknownKids.clear();
}

function unauthorizedResponse() {
  return new Response(JSON.stringify({ error: 'unauthorized' }), {
    status: 401,
    headers: { 'Content-Type': 'application/json' },
  });
}

function base64UrlToBytes(value) {
  if (typeof value !== 'string' || value.length === 0) {
    throw new Error('invalid-base64url');
  }

  const padded = value.replaceAll('-', '+').replaceAll('_', '/');
  const padLength = (4 - (padded.length % 4)) % 4;
  const binary = atob(padded + '='.repeat(padLength));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i);
  }

  return bytes;
}

function decodeJsonPart(part) {
  const json = new TextDecoder().decode(base64UrlToBytes(part));
  return JSON.parse(json);
}

function audienceMatches(aud, expected) {
  if (typeof aud === 'string') {
    return aud === expected;
  }

  return Array.isArray(aud) && aud.length === 1 && aud[0] === expected;
}

function parseCacheTtlMs(response, fallbackMs) {
  const cacheControl = response.headers?.get?.('cache-control');
  if (typeof cacheControl !== 'string') {
    return fallbackMs;
  }

  const match = /max-age=(\d+)/i.exec(cacheControl);
  if (!match) {
    return fallbackMs;
  }

  const seconds = Number.parseInt(match[1], 10);
  if (!Number.isFinite(seconds) || seconds <= 0) {
    return fallbackMs;
  }

  return Math.min(seconds * 1000, fallbackMs);
}

async function fetchJwks(url, fetchImpl) {
  const response = await fetchImpl(url, {
    method: 'GET',
    headers: { Accept: 'application/json' },
  });

  if (!response.ok) {
    throw new Error('jwks-fetch-failed');
  }

  const body = await response.json();
  if (!body || !Array.isArray(body.keys)) {
    throw new Error('jwks-invalid');
  }

  /** @type {Map<string, JsonWebKey>} */
  const keys = new Map();
  for (const key of body.keys) {
    if (
      key &&
      typeof key === 'object' &&
      typeof key.kid === 'string' &&
      key.kid &&
      key.kty === 'RSA' &&
      typeof key.n === 'string' &&
      typeof key.e === 'string'
    ) {
      keys.set(key.kid, {
        kty: 'RSA',
        kid: key.kid,
        n: key.n,
        e: key.e,
        alg: typeof key.alg === 'string' ? key.alg : 'RS256',
        use: typeof key.use === 'string' ? key.use : 'sig',
      });
    }
  }

  if (keys.size === 0) {
    throw new Error('jwks-empty');
  }

  return {
    keys,
    ttlMs: parseCacheTtlMs(response, DEFAULT_JWKS_TTL_MS),
  };
}

async function resolveJwk(kid, options) {
  const url = options.jwksUrl ?? FIREBASE_JWKS_URL;
  const fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
  const nowMs = options.nowMs ?? Date.now();
  const cacheHolder = options.cacheHolder ?? defaultJwksState;
  cacheHolder.refreshPromise ??= null;
  cacheHolder.lastRefreshAt ??= 0;
  cacheHolder.unknownKids ??= new Map();

  const cached = cacheHolder.current;
  if (cached && cached.url === url && cached.expiresAt > nowMs && cached.keys.has(kid)) {
    return cached.keys.get(kid);
  }

  for (const [unknownKid, expiresAt] of cacheHolder.unknownKids) {
    if (expiresAt <= nowMs) {
      cacheHolder.unknownKids.delete(unknownKid);
    }
  }
  if ((cacheHolder.unknownKids.get(kid) ?? 0) > nowMs) {
    throw new Error('unknown-kid');
  }

  const cacheIsFresh = cached && cached.url === url && cached.expiresAt > nowMs;
  if (cacheIsFresh && nowMs - cacheHolder.lastRefreshAt < JWKS_REFRESH_COOLDOWN_MS) {
    rememberUnknownKid(cacheHolder.unknownKids, kid, nowMs);
    throw new Error('unknown-kid');
  }

  if (!cacheHolder.refreshPromise) {
    cacheHolder.refreshPromise = fetchJwks(url, fetchImpl)
      .then((fresh) => {
        const nextCache = {
          url,
          expiresAt: nowMs + fresh.ttlMs,
          keys: fresh.keys,
        };
        cacheHolder.current = nextCache;
        cacheHolder.lastRefreshAt = nowMs;
        return nextCache;
      })
      .finally(() => {
        cacheHolder.refreshPromise = null;
      });
  }

  const nextCache = await cacheHolder.refreshPromise;

  const jwk = nextCache.keys.get(kid);
  if (!jwk) {
    rememberUnknownKid(cacheHolder.unknownKids, kid, nowMs);
    throw new Error('unknown-kid');
  }

  return jwk;
}

function rememberUnknownKid(unknownKids, kid, nowMs) {
  if (unknownKids.size >= MAX_NEGATIVE_KIDS && !unknownKids.has(kid)) {
    unknownKids.delete(unknownKids.keys().next().value);
  }
  unknownKids.set(kid, nowMs + UNKNOWN_KID_TTL_MS);
}

async function importVerifyKey(jwk) {
  return crypto.subtle.importKey(
    'jwk',
    {
      kty: 'RSA',
      n: jwk.n,
      e: jwk.e,
      alg: 'RS256',
      ext: true,
    },
    { name: 'RSASSA-PKCS1-v1_5', hash: 'SHA-256' },
    false,
    ['verify'],
  );
}

/**
 * Verifies a Firebase ID token and returns the Firebase UID.
 *
 * @param {string} token
 * @param {{
 *   projectId?: string,
 *   jwksUrl?: string,
 *   fetch?: typeof fetch,
 *   nowMs?: number,
 *   clockSkewSeconds?: number,
 *   cacheHolder?: { current: object | null, refreshPromise?: Promise<object> | null, lastRefreshAt?: number, unknownKids?: Map<string, number> },
 * }} [options]
 * @returns {Promise<{ uid: string, payload: Record<string, unknown> }>}
 */
export async function verifyFirebaseIdToken(token, options = {}) {
  if (typeof token !== 'string' || token.length === 0 || token.length > MAX_TOKEN_CHARS) {
    throw new Error('invalid-token');
  }

  const parts = token.split('.');
  if (parts.length !== 3 || parts.some((part) => part.length === 0)) {
    throw new Error('invalid-token');
  }

  const [headerPart, payloadPart, signaturePart] = parts;
  let header;
  try {
    header = decodeJsonPart(headerPart);
  } catch {
    throw new Error('invalid-header');
  }

  if (!header || typeof header !== 'object' || header.alg !== 'RS256') {
    throw new Error('invalid-alg');
  }

  if (typeof header.kid !== 'string' || header.kid.length === 0) {
    throw new Error('missing-kid');
  }

  const jwk = await resolveJwk(header.kid, options);
  const key = await importVerifyKey(jwk);
  const signature = base64UrlToBytes(signaturePart);
  const signedData = textEncoder.encode(`${headerPart}.${payloadPart}`);
  const valid = await crypto.subtle.verify(
    { name: 'RSASSA-PKCS1-v1_5' },
    key,
    signature,
    signedData,
  );

  if (!valid) {
    throw new Error('bad-signature');
  }

  let payload;
  try {
    payload = decodeJsonPart(payloadPart);
  } catch {
    throw new Error('invalid-payload');
  }

  if (!payload || typeof payload !== 'object') {
    throw new Error('invalid-payload');
  }

  const projectId = options.projectId ?? FIREBASE_PROJECT_ID;
  const expectedIssuer = `https://securetoken.google.com/${projectId}`;
  const skewSeconds = options.clockSkewSeconds ?? DEFAULT_CLOCK_SKEW_SECONDS;
  const nowSeconds = Math.floor((options.nowMs ?? Date.now()) / 1000);

  if (!audienceMatches(payload.aud, projectId)) {
    throw new Error('bad-aud');
  }

  if (payload.iss !== expectedIssuer) {
    throw new Error('bad-iss');
  }

  if (typeof payload.exp !== 'number' || !Number.isFinite(payload.exp)) {
    throw new Error('bad-exp');
  }

  if (nowSeconds >= payload.exp + skewSeconds) {
    throw new Error('expired');
  }

  if (typeof payload.iat === 'number' && Number.isFinite(payload.iat)) {
    if (payload.iat > nowSeconds + skewSeconds) {
      throw new Error('bad-iat');
    }
  }

  if (typeof payload.sub !== 'string' || payload.sub.length === 0 || payload.sub.length > 128) {
    throw new Error('bad-sub');
  }

  return { uid: payload.sub, payload };
}

/**
 * Request helper for future product routes. Fail-closed with a generic 401.
 *
 * @param {Request} request
 * @param {Parameters<typeof verifyFirebaseIdToken>[1]} [options]
 * @returns {Promise<{ authorized: true, uid: string, emailVerified: boolean } | { authorized: false, response: Response }>}
 */
export async function requireFirebaseUser(request, options = {}) {
  const header = request.headers.get('Authorization');
  if (typeof header !== 'string') {
    return { authorized: false, response: unauthorizedResponse() };
  }

  const match = /^Bearer\s+(\S+)$/i.exec(header.trim());
  if (!match) {
    return { authorized: false, response: unauthorizedResponse() };
  }

  try {
    const { uid, payload } = await verifyFirebaseIdToken(match[1], options);
    return { authorized: true, uid, emailVerified: payload.email_verified === true };
  } catch {
    return { authorized: false, response: unauthorizedResponse() };
  }
}
