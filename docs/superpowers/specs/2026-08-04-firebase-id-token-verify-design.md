# Firebase ID Token verification on the Worker

## Goal

Close the `PROJECT_STATE` pending item: before any authenticated product route
exists, the Cloudflare Worker must be able to verify Firebase Authentication
ID tokens over HTTPS and expose only the Firebase UID (`sub`) as the permanent
internal identifier.

## Scope

In scope:

- Pure verification module under `infra/cloudflare-worker/src/auth/`
- `requireFirebaseUser(request)` helper matching the admin auth style
- Unit tests with a fixed RSA key pair and mocked JWKS `fetch`
- README / architecture note that the verifier is ready for future routes
- Document that old Worker product accounts are not migrated

Out of scope:

- Product routes (including `/account/me`)
- Desktop client sending `Authorization: Bearer`
- Deploy / remote D1 changes
- DROP of legacy `user_*` tables
- Accepting email as an identifier

## Constants

| Item | Value |
|------|--------|
| Project ID | `fivemcleaner-app` |
| `aud` | `fivemcleaner-app` |
| `iss` | `https://securetoken.google.com/fivemcleaner-app` |
| Algorithm | RS256 only |
| JWKS | `https://www.googleapis.com/service_accounts/v1/jwk/securetoken@system.gserviceaccount.com` |

## Design

### `verifyFirebaseIdToken(token, options)`

1. Reject non-string / oversized tokens (fail closed).
2. Split JWT into three base64url parts; reject otherwise.
3. Parse header JSON: require `alg === "RS256"` and a non-empty `kid`.
4. Resolve the JWK for `kid` from an in-memory JWKS cache (default TTL 1h).
   On miss or unknown kid after a fresh fetch, fail closed.
5. Import the JWK via `crypto.subtle.importKey` and
   `crypto.subtle.verify` (`RSASSA-PKCS1-v1_5` + SHA-256) over
   `header.payload`.
6. Parse payload JSON and require:
   - `aud === fivemcleaner-app` (string or single-element array)
   - `iss === https://securetoken.google.com/fivemcleaner-app`
   - `exp` numeric and `now < exp + skew`
   - `sub` non-empty string (returned as `uid`)
7. Optional clock skew default: 60 seconds.
8. Never log the token. Errors are opaque to callers of the HTTP helper.

### `requireFirebaseUser(request, options)`

- Read `Authorization`.
- Require exact `Bearer <token>` shape (single scheme, non-empty token).
- On success: `{ authorized: true, uid }`.
- On any failure: `{ authorized: false, response }` with HTTP 401 and
  `{ "error": "unauthorized" }` JSON. No claim-level detail in the body.

### Cache / testability

- Module-level JWKS cache with `expiresAt`.
- `options.fetch`, `options.now`, `options.projectId`, `options.jwksUrl`,
  `options.clockSkewSeconds`, and `options.cache` are injectable for tests.
- No npm crypto dependencies; Web Crypto only (same constraint as `crypto.js`).

## Legacy Worker accounts

Firebase replaced the old Worker product-account system. There is no automatic
migration of `user_accounts` / sessions. For this project there are no real
users to preserve: document “no migration”; remote table cleanup waits for an
authorized deploy/migration task.

## Success criteria

- Unit tests cover valid token, bad signature, wrong aud/iss, expired, missing
  bearer, unknown kid with JWKS refresh, and non-RS256 header.
- `npm test` in `infra/cloudflare-worker` passes.
- No product route is registered; verifier is imported only by tests until a
  future authenticated route lands.
