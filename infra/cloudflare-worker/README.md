# FiveMCleaner telemetry + dashboard API Worker (scaffold — not deployed)

This is the Cloudflare Worker + D1 scaffold for the anonymous telemetry
pipeline described in [`docs/telemetry.md`](../../docs/telemetry.md), plus
the authenticated stats API the [dashboard](../dashboard/README.md) reads
from. It is **not deployed** and the .NET client does **not** send to it
yet — telemetry is still sent through FormSubmit, unchanged. This directory
exists so the Worker code, schema, auth, and validation can be reviewed and
tested ahead of a future increment that switches the client's transport
over.

## What's here

- `wrangler.toml` — Worker config with `Development`/`Production` environment
  sections. Both currently bind the **same** D1 database (only one was
  provisioned); rows are tagged with an `environment` column instead of using
  two databases.
- `schema.sql` — the D1 tables: `telemetry_events` (one row per optimization
  run, including the version-2-consent hardware profile), 
  `telemetry_event_actions` (one row per applied action ID, for "most used
  function"), `login_attempts` and `admin_sessions` (custom dashboard auth).
- `src/validateEvent.js` — pure, dependency-free validation of one event or a
  batch. The Worker never trusts client-side validation alone; every field is
  re-checked against the same allowlist server-side.
- `src/index.js` — routes: `POST /telemetry` (ingest), `POST /admin/login` /
  `POST /admin/logout`, `GET /api/stats/:name[.csv]` (protected), plus CORS
  handling (`src/cors.js`) for every response since the dashboard is served
  from a different origin than this Worker.
- `src/auth/` — the custom admin authentication (see below).
- `src/stats/` — `queries.js` (pure SQL+params builders, one per dashboard
  chart) and `csv.js` (pure CSV serialization for the export feature).
  Available `:name` values: `runs-per-day`, `os-versions`, `app-versions`,
  `profiles`, `top-actions`, `top-cpu`, `top-gpu`, `ram-buckets`,
  `average-time`, `success-rate`, `error-categories`,
  `top-actions-in-failures`, `errors-by-version`, `recent-failures`. Every
  one accepts `?from=&to=&version=&environment=` query filters (`environment`
  defaults to `Production`; pass `All` to look across both).
- `test/` — unit tests for everything pure-logic above, run with Node's
  built-in test runner (no Miniflare/wrangler required):

  ```bash
  npm test
  ```

## Admin dashboard authentication

Per an explicit decision (no external domain, no Cloudflare Access, no
Google/GitHub OAuth — the dashboard is served from a plain `*.pages.dev`
URL), authentication is a small, self-contained system:

- **Password**: never stored in code or in `wrangler.toml`. Run
  `npm run hash-admin-password` locally, which prompts for a password and
  prints a self-contained `pbkdf2$<iterations>$<salt>$<hash>` string (PBKDF2-
  SHA256, 210,000 iterations, via the Workers-native `crypto.subtle` — no
  third-party crypto dependency). That string, and only that string, becomes
  the `ADMIN_PASSWORD_HASH` Worker secret (`wrangler secret put
  ADMIN_PASSWORD_HASH`). The plaintext password is never written to disk,
  committed, or logged.
- **Brute-force protection**: `login_attempts` tracks failed logins per
  HMAC'd IP (`src/auth/bruteForceGuard.js`, keyed by the `IP_HASH_SECRET`
  Worker secret — the real IP itself is never stored). Five failed attempts
  within 15 minutes locks that IP out for 15 minutes; the counter resets once
  the window passes.
- **Sessions**: server-side, revocable (`admin_sessions`, `src/auth/
  sessionStore.js`) — a random 256-bit session ID is the *only* thing stored
  in the browser cookie (`HttpOnly`, `Secure`, `SameSite=Strict`), so logout
  or manually clearing the table actually invalidates it immediately, unlike
  a stateless signed token that can only be waited out.
- **Swappable by design**: `src/auth/passwordAuthProvider.js` exposes exactly
  three functions — `login`, `logout`, `requireSession` — and `index.js` only
  ever calls those three. A future OAuth-based provider (Google/GitHub, or
  Cloudflare Access) only needs to implement the same three functions with
  the same signatures; no route or stats-endpoint code would need to change.

**Known test gap**: the pure decision logic behind each of these
(`crypto.js`, `bruteForceGuard.js`, `sessionStore.js`, `stats/queries.js`,
`stats/csv.js`) is unit tested. The D1-touching glue in
`passwordAuthProvider.js` and the routing in `index.js` are not covered by an
automated test — that would require Miniflare (a simulated Workers/D1
runtime), which was not set up in this environment. Review those two files
manually, and validate them for real against `wrangler dev --local` before
relying on them in production.

## What is intentionally not done yet

- **No deploy.** `npm run deploy:development` / `deploy:production` exist for
  when deployment is explicitly authorized, but neither has been run from
  this environment.
- **No client wiring for the stats-producing pipeline.** The .NET app has
  code ready for this transport (`CloudflareTelemetryService.cs`) but it
  stays inactive until `TelemetryEndpoint` is configured post-deploy — see
  `docs/telemetry.md`.
- **No local D1 migration applied**, and no secrets have been set anywhere.

## Applying the schema, setting secrets, and deploying (future step, requires authorization)

```bash
npm install
npm run db:migrate:local                      # local-only, safe to run anytime

npm run hash-admin-password                    # prints the ADMIN_PASSWORD_HASH value
wrangler secret put ADMIN_PASSWORD_HASH --env development
wrangler secret put ADMIN_PASSWORD_HASH --env production
wrangler secret put IP_HASH_SECRET --env development   # any long random string
wrangler secret put IP_HASH_SECRET --env production

wrangler d1 execute fivemcleaner-telemetry --env development --remote --file=./schema.sql   # touches the real database — ask first
npm run deploy:development   # touches Cloudflare — ask first
npm run deploy:production    # touches Cloudflare — ask first
```
