# FiveMCleaner dashboard

Static admin dashboard for the telemetry collected by
[`infra/cloudflare-worker`](../cloudflare-worker/README.md). Plain HTML/CSS/JS,
no build step, no framework — served as-is by Cloudflare Pages. There is no
real data to show yet since the .NET client does not send telemetry to the
Worker either (still FormSubmit); the dashboard is fully functional and
tested, just waiting for the client transport to be switched over in a
future step.

## What's here

- `index.html` — login screen + the dashboard itself (one page, toggled by
  whether a session cookie is currently valid), branded with the FiveMCleaner
  logo, and organized into three sections: **Adoção** (usage/version/profile
  charts), **Hardware** (CPU/GPU/RAM breakdowns), and **Diagnóstico de bugs**
  (error categories, actions most associated with failures, errors by
  version, and a raw "últimos erros" table for spotting a fresh bug without
  waiting for it to show up in an aggregate).
- `assets/img/logo.png` — the app's own icon, reused as-is (same asset as
  `website/public-site/icon.png`).
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

## Deploying (future step, requires authorization)

```bash
npx wrangler pages deploy . --project-name=fivemcleaner-dashboard
```

Point the Pages project's custom domain (e.g. `dashboard.fivemcleaner.com`)
and the Worker's route at each other once both are deployed — neither step
has been run from this environment.
