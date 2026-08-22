# FiveMCleaner dashboard

Static admin dashboard for the telemetry and bug reports collected by
[`infra/cloudflare-worker`](../cloudflare-worker/README.md). Plain HTML/CSS/JS,
no build step, no framework — served as-is by Cloudflare Pages. The .NET
client sends telemetry to the Worker's `/telemetry` route and bug reports to
`/bugs`, both live and deployed. Bug reports are text-only (no
attachment/screenshot, no R2) — see `infra/cloudflare-worker/README.md`.

## What's here

- `index.html` — login screen + the dashboard itself (one page, toggled by
  whether a session cookie is currently valid), branded with the FiveMCleaner
  logo, and organized into four sections: **Adoção** (usage/version/profile
  charts), **Hardware** (CPU/GPU/RAM breakdowns), **Diagnóstico de bugs**
  (error categories, actions most associated with failures, errors by
  version, and a raw "últimos erros" table for spotting a fresh bug without
  waiting for it to show up in an aggregate), and **Bugs reportados** (the
  "Reportar um bug" submissions from `/api/bugs` — category, summary,
  version, profile, environment, optional email, and whether a log excerpt
  was included; no attachment/screenshot, that feature was dropped).
- `assets/img/logo.png` — the app's own icon, reused as-is (same asset as
  `website/public/icon.png`).
- `assets/api.js` — pure URL-building and response-shaping for the Worker's
  `/api/stats/*` endpoints. Unit tested (`test/api.test.js`).
- `assets/charts.js` — pure data-shaping (turning raw stat rows into
  chart-ready series, formatting durations/percentages/timestamps, mapping a
  `recentFailures` row into the raw-feed table's columns). Unit tested
  (`test/charts.test.js`).
- `assets/rendering.js` — canvas drawing (bar/line charts). Touches the DOM
  directly, so unlike the two files above it is **not** covered by an
  automated test (no headless-canvas dependency was introduced for that) —
  verify visually once deployed.
- `assets/app.js` — DOM wiring: login/logout, filters (date range, version,
  environment), fetching every stat, drawing every chart, rendering the
  recent-failures table, and the CSV export links. Thin glue over the tested
  modules above.
- `_headers` — Cloudflare Pages headers that forbid framing, plugins and
  third-party scripts while allowing requests only to the deployed Worker.

Filters include an **Ambiente** selector (Produção/Desenvolvimento/Todos) so
the dashboard can look across environments when debugging the pipeline
itself, even though every chart defaults to Produção-only to avoid mixing a
developer's own test runs into what the numbers say about real users.

Run the pure-logic tests:

```bash
npm test
```

## Authentication

The dashboard has no login logic of its own — it just posts the password to
the Worker's `/admin/login` and relies on the `HttpOnly` session cookie the
Worker sets. See
[`infra/cloudflare-worker/README.md`](../cloudflare-worker/README.md) for the
full auth design (custom password + PBKDF2 hash + brute-force lockout +
server-side revocable sessions — no Google/GitHub OAuth, no Cloudflare
Access, no custom domain required).

## "Active users" honesty note

FiveMCleaner's telemetry never includes a device or machine identifier (see
`docs/telemetry.md`) — that is a deliberate privacy invariant, not a gap. As
a direct consequence, this dashboard cannot show a true unique-user count;
every "per day"/"in period" number is a count of *optimization runs*
(events), which the UI and this README say plainly rather than mislabeling
it as "usuários online" the way an early sketch of this dashboard did.

## Re-deploying

```bash
npx wrangler pages deploy . --project-name=fivemcleaner-dashboard
```

`assets/app.js` hardcodes the Worker's `workers.dev` URL as the default API
base (no custom domain connects the two, so `location.origin` would point
at the dashboard's own, wrong origin) — update that constant first if the
Worker is ever redeployed under a different URL. No custom domain (e.g.
`dashboard.fivemcleaner.com`) has been configured; ask before adding one, as
it requires DNS changes to a real zone.
