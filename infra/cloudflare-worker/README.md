# FiveMCleaner telemetry Worker (scaffold — not deployed)

This is the Cloudflare Worker + D1 scaffold for the anonymous telemetry
pipeline described in [`docs/telemetry.md`](../../docs/telemetry.md). It is
**not deployed** and the .NET client does **not** send to it yet — telemetry
is still sent through FormSubmit, unchanged. This directory exists so the
Worker code, schema, and validation can be reviewed and tested ahead of a
future increment that adds the local queue and HTTP batch transport on the
client side.

## What's here

- `wrangler.toml` — Worker config with `Development`/`Production` environment
  sections. Both currently bind the **same** D1 database (only one was
  provisioned); rows are tagged with an `environment` column instead of using
  two databases.
- `schema.sql` — the D1 table. Columns mirror the closed allowlist already
  documented for anonymous telemetry: no paths, no machine identifiers, no
  free text.
- `src/validateEvent.js` — pure, dependency-free validation of one event or a
  batch. The Worker never trusts client-side validation alone; every field is
  re-checked against the same allowlist server-side.
- `src/index.js` — the Worker's `fetch` handler: validates the batch, inserts
  it into D1, and returns `202` on success or `400`/`405` otherwise.
- `test/validateEvent.test.js` — unit tests for the validation logic, run
  with Node's built-in test runner (no Miniflare/wrangler required):

  ```bash
  npm test
  ```

## What is intentionally not done yet

- **No deploy.** `npm run deploy:development` / `deploy:production` exist for
  when deployment is explicitly authorized, but neither has been run from
  this environment.
- **No client wiring.** The .NET app has no code that calls this Worker.
- **No local D1 migration applied.** `npm run db:migrate:local` runs
  `schema.sql` against a *local* (Miniflare-simulated) D1 instance for
  development, and does not touch the real, remote database in Cloudflare.

## Applying the schema and deploying (future step, requires authorization)

```bash
npm install
npm run db:migrate:local     # local-only, safe to run anytime
wrangler d1 execute fivemcleaner-telemetry --env development --remote --file=./schema.sql   # touches the real database — ask first
npm run deploy:development   # touches Cloudflare — ask first
npm run deploy:production    # touches Cloudflare — ask first
```
