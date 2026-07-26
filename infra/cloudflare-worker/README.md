# FiveMCleaner telemetry + dashboard API Worker

**Deployed** at `https://fivemcleaner-telemetry.felipemarquesini10.workers.dev`.

This is the Cloudflare Worker + D1 backend for the anonymous telemetry
pipeline described in [`docs/telemetry.md`](../../docs/telemetry.md) and the
bug-report pipeline described in
[`docs/bug-reports.md`](../../docs/bug-reports.md), plus the authenticated
stats/bugs API the [dashboard](../dashboard/README.md) reads from.
FormSubmit has been fully removed from the .NET client for both telemetry
and bug reports — this Worker is the only transport for both.

Both `/telemetry` and `/bugs` are **live and deployed**. Bug reports are
text-only — no attachment/screenshot support, no R2 dependency. That
feature was dropped after R2 turned out to require an account-level
activation in the Cloudflare Dashboard (accepting R2's terms, possibly
confirming billing even on the free tier) that couldn't be done from this
environment; rather than block on that, the report form stayed D1-only
(category, summary, description, app version, profile, optional technical
summary, optional email, optional plain-text log excerpt capped at 100 KB).

## What's here

- `wrangler.toml` — Worker config. A single deployed instance (the
  top-level, unnamed environment — `wrangler deploy` with no `--env`)
  handles both Development- and Production-tagged telemetry; the
  `environment` column on each row is what separates them for the
  dashboard's filters, not a second deployment. `env.development`/
  `env.production` sections exist but are unused today — kept in case a
  genuine need for physically separate deployments comes up later.
- `schema.sql` — the D1 tables: `telemetry_events` (one row per optimization
  run, including the version-2-consent hardware profile), 
  `telemetry_event_actions` (one row per applied action ID, for "most used
  function"), `login_attempts` and `admin_sessions` (custom dashboard auth).
  Applied to the real database already.
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
  prints a self-contained `pbkdf2$<iterations>$<salt>$<hash>` string
  (PBKDF2-SHA256 via the Workers-native `crypto.subtle` — no third-party
  crypto dependency). **100,000 iterations**, not the OWASP-recommended
  210,000: the Workers runtime (BoringSSL, not Node's OpenSSL) hard-caps
  PBKDF2 at 100,000 and throws `NotSupportedError` above that — found only
  once actually deployed, since Node itself has no such cap and the test
  suite runs under Node. 100,000 remains an accepted OWASP baseline for
  PBKDF2-SHA256. That hash string, and only that string, becomes the
  `ADMIN_PASSWORD_HASH` Worker secret (`wrangler secret put
  ADMIN_PASSWORD_HASH`). The plaintext password is never written to disk,
  committed, or logged.
- **Brute-force protection**: `login_attempts` tracks failed logins per
  HMAC'd IP (`src/auth/bruteForceGuard.js`, keyed by the `IP_HASH_SECRET`
  Worker secret — the real IP itself is never stored). Five failed attempts
  within 15 minutes locks that IP out for 15 minutes; the counter resets once
  the window passes.
- **Sessions**: server-side, revocable (`admin_sessions`, `src/auth/
  sessionStore.js`) — a random 256-bit session ID is the *only* thing stored
  in the browser cookie (`HttpOnly`, `Secure`, `SameSite=None`), so logout
  or manually clearing the table actually invalidates it immediately, unlike
  a stateless signed token that can only be waited out. `SameSite=None`
  (not `Strict`/`Lax`) is required because the dashboard (`*.pages.dev`) and
  this Worker (`*.workers.dev`) are genuinely different registrable
  domains — a stricter policy silently never sends the cookie back on a
  cross-site `fetch`, which is exactly what made the first deployment's
  login appear to succeed but leave the dashboard stuck on the login screen.
- **Swappable by design**: `src/auth/passwordAuthProvider.js` exposes exactly
  three functions — `login`, `logout`, `requireSession` — and `index.js` only
  ever calls those three. A future OAuth-based provider (Google/GitHub, or
  Cloudflare Access) only needs to implement the same three functions with
  the same signatures; no route or stats-endpoint code would need to change.

**Known test gap**: the pure decision logic behind each of these
(`crypto.js`, `bruteForceGuard.js`, `sessionStore.js`, `stats/queries.js`,
`stats/csv.js`, `cors.js`) is unit tested. The D1-touching glue in
`passwordAuthProvider.js` and the routing in `index.js` are not covered by an
automated test — that would require Miniflare (a simulated Workers/D1
runtime), which was not set up in this environment. Both were validated
manually against the real deployment (see "Verified end-to-end" below); two
real bugs (the PBKDF2 iteration cap and the `SameSite` cookie policy) were
only caught that way, not by the unit tests, which is exactly why this gap
is called out rather than assumed harmless.

## Verified end-to-end

Confirmed against the real, deployed Worker + dashboard: sent a test
telemetry event via `curl`, logged in through the actual browser at
`https://fivemcleaner-dashboard.pages.dev`, and saw the event reflected in
the tiles and charts (then deleted that test row from the real database —
no test data was left behind).

## Deploying and rotating secrets

```bash
npm install
npm run db:migrate:local          # local-only, safe to run anytime

npm run hash-admin-password       # prints the ADMIN_PASSWORD_HASH value
wrangler secret put ADMIN_PASSWORD_HASH
wrangler secret put IP_HASH_SECRET   # any long random string

wrangler d1 execute fivemcleaner-telemetry --remote --file=./schema.sql   # touches the real database — ask first
wrangler deploy   # touches Cloudflare — ask first
```

The `env.development`/`env.production` deploy scripts in `package.json`
(`deploy:development`/`deploy:production`) target the currently-unused
named-environment sections mentioned above; the real deployment uses plain
`wrangler deploy`/`npm run deploy` with no `--env`.
