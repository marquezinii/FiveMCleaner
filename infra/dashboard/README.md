# Ralven dashboard

Static admin dashboard for the telemetry and bug reports collected by
[`infra/cloudflare-worker`](../cloudflare-worker/README.md). Plain HTML/CSS/JS,
no build step, no framework — served as-is by Cloudflare Pages. The .NET
client sends telemetry to the Worker's `/telemetry` route and bug reports to
`/bugs`, both live and deployed. Bug reports are text-only (no
attachment/screenshot, no R2) — see `infra/cloudflare-worker/README.md`.

## What's here

- `index.html` — login screen + the dashboard itself (one page, toggled by
  whether a session cookie is currently valid), branded with the Ralven
  logo, and organized into four sections: **Adoção** (usage/version/profile
  charts), **Hardware** (CPU/GPU/RAM breakdowns), **Diagnóstico de bugs**
  (error categories, exact allowlisted failure codes, errors by version, and
  a recent incident feed with occurrence metadata and planned action IDs),
  and **Bugs reportados** (the
  "Reportar um bug" submissions from `/api/bugs` — category, summary,
  version, profile, environment, optional email, and whether a log excerpt
  was included; no attachment/screenshot, that feature was dropped). Recent
  errors and reports open a native detail dialog so long descriptions,
  technical context and log excerpts remain readable without widening the
  tables.
- `assets/img/logo.png` — the app's own icon, reused as-is (same asset as
  `assets/brand/export/app-icon/ralven-app-icon-512.png`).
- `assets/api.js` — pure URL-building and response-shaping for the Worker's
  `/api/stats/*` endpoints. Unit tested (`test/api.test.js`).
- `assets/charts.js` — pure data-shaping (turning raw stat rows into
  chart-ready series, formatting durations/percentages/timestamps, mapping a
  `recentFailures` row into the raw-feed table's columns). Unit tested
  (`test/charts.test.js`).
- `assets/rendering.js` — responsive, high-DPI canvas drawing (bar/line/donut
  charts), with pointer tooltips, keyboard exploration and resize handling.
  Pure hit-testing and tooltip formatting are unit tested without adding a
  headless-canvas dependency; final rendering still requires browser QA.
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
the Worker's `/admin/login` and relies on the 30-day `HttpOnly` session cookie
the Worker sets. It never stores the password; logging out or clearing the
site data ends access. See
[`infra/cloudflare-worker/README.md`](../cloudflare-worker/README.md) for the
full auth design (custom password + PBKDF2 hash + brute-force lockout +
server-side revocable sessions — no Google/GitHub OAuth, no Cloudflare
Access, no custom domain required).

## "Active users" honesty note

Ralven's telemetry never includes a device or machine identifier (see
`docs/telemetry.md`) — that is a deliberate privacy invariant, not a gap. As
a direct consequence, this dashboard cannot show a true unique-user count;
every "per day"/"in period" number is a count of *optimization runs*
(events), which the UI and this README say plainly rather than mislabeling
it as "usuários online" the way an early sketch of this dashboard did.

## Re-deploying

```bash
npx wrangler pages project create ralven-dashboard --production-branch=production # one-time cutover only
npx wrangler pages deploy . --project-name=ralven-dashboard --branch=production
```

`assets/app.js` usa `https://api.vemryx.com` como API padrão; `location.origin`
continua sendo o endereço do próprio dashboard. O nome do projeto Pages e os
identificadores legados do Worker são mantidos somente por compatibilidade com
clientes já publicados. O endereço do dashboard do Ralven é
`https://ralven-dashboard.pages.dev`. During the cutover, the previous
`dashboard.vemryx.com` and `fivemcleaner-dashboard.pages.dev` origins remain in
the Worker CORS allowlist so existing sessions and bookmarks do not break before
the new Pages project is deployed and verified.
