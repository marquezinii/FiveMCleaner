# Ralven dashboard

Static admin command center for the operational data collected by
[`infra/cloudflare-worker`](../cloudflare-worker/README.md). Plain HTML/CSS/JS,
no build step, no framework — served as-is by Cloudflare Pages. The .NET
client sends telemetry to the Worker's `/telemetry` route and bug reports to
`/bugs`, both live and deployed. Bug reports are text-only (no
attachment/screenshot, no R2) — see `infra/cloudflare-worker/README.md`.

## What's here

- `index.html` — login screen + responsive command center organized into
  **Visão geral**, **Crescimento**, **Confiabilidade**, **Compatibilidade** and
  **Operações**. It combines anonymous optimization telemetry with aggregate
  account growth, Ralven AI usage/cost, subscription/payment health, updater
  outcomes and support reports. Recent errors and reports open a native detail
  dialog; incident search and global period/version/environment filters keep
  investigation focused.
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
- `assets/app.js` — DOM wiring: persistent session recovery, period presets,
  global filters, aggregate health signals, charts, searchable incident feeds,
  CSV exports and the live-alert workflow. Publishing or deactivating a live
  alert requires an explicit confirmation and the existing session-bound CSRF
  token.
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

## Growth and "active users" honesty note

Ralven's telemetry never includes a device or machine identifier (see
`docs/telemetry.md`) — that is a deliberate privacy invariant, not a gap. As
a direct consequence, this dashboard cannot show a true unique-user count;
every "per day"/"in period" number is a count of *optimization runs*
(events), which the UI and this README say plainly rather than mislabeling
it as "usuários online" the way an early sketch of this dashboard did.

The account total and new-account series come from aggregate queries over
`account_profiles`. "Contas ativas no Ralven AI" means distinct authenticated
accounts that made an AI request in the selected period; it is not presented as
general app activity. No endpoint returns UID, username, e-mail, provider ID or
interactive AI content.

## Deploy

O push de uma tag estável dispara o workflow de release. Depois dos gates dos
ambientes `release-signing` e `production`, ele publica somente `index.html`,
`_headers` e `assets/` com `--project-name=ralven-dashboard` e confirma no
endereço público o commit implantado. Para
retomar uma publicação interrompida, use o `workflow_dispatch` com a mesma tag
e `publish=true`; o fluxo repete as validações e preserva os mesmos gates.

`assets/app.js` usa `https://api.vemryx.com` como API padrão; `location.origin`
continua sendo o endereço do próprio dashboard. O nome do projeto Pages e os
identificadores legados do Worker são mantidos somente por compatibilidade com
clientes já publicados. O endereço do dashboard do Ralven é
`https://ralven-dashboard.pages.dev`. During the cutover, the previous
`dashboard.vemryx.com` and `fivemcleaner-dashboard.pages.dev` origins remain in
the Worker CORS allowlist so existing sessions and bookmarks do not break before
the new Pages project is deployed and verified.
