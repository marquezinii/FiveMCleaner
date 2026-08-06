# Tech-debt pass — decisions log

- Integration status: integrated into `dev/proxima-versao` on 02/08/2026.

Working branch/worktree: `ai/opencode/tech-debt`. Rule applied throughout:
refactors are behavior-preserving unless explicitly recorded below as a
deliberate behavior change.

## Executed work

- Dashboard guards hardened (`infra/dashboard/assets/app.js`): the
  total-failures tile and recent-failures table now guard against any
  unauthorized/error response from the worker, not only `{ ok: false }` with
  `.unauthorized`.
- Dashboard dedupe: the three identical local fallback helpers in
  `charts.js` collapsed into one module-level helper; the six-color donut
  palette duplicated between `rendering.js` and `app.js` collapsed into a
  shared `DONUT_COLORS` constant used by both the renderer and the chart
  definition.
- Worker `src/filters.js`: `appendEnvironmentClause` / `appendDateRangeClauses`
  shared by the stats, bug-report and updater-event query builders. Each
  domain keeps its own clause order, defaults, limits and WHERE/1=1
  formatting — only the two common clauses were extracted.
- Worker `src/environments.js`: single `ALLOWED_ENVIRONMENTS` set now used by
  all three validators (updater events previously inlined the same list).
- Worker `src/readJsonBody.js`: parse-failed sentinel shared by the three
  ingest routes.
- Worker `src/releaseManifest.js`: signed release-manifest validation moved
  out of `index.js`; the handler keeps the 503-vs-500 response distinction.
- Worker config: removed dead `env.development`/`env.production` sections
  from `wrangler.toml`, the `deploy:*` npm scripts, the `--env` flags in
  `hash-admin-password.mjs`, and the README explanation. `.dev.vars`
  regenerated at 100k iterations (the Workers runtime PBKDF2 cap) and added
  to the main repo; `.dev.vars` is git-ignored.
- CI/scripts: `-SkipTests` now passed to `Verify-Safety.ps1` where the
  caller runs `dotnet test` immediately after (`Build-Portable.ps1`,
  `ci.yml`, `release.yml`); `Assert-UnderArtifacts` and `Get-ProjectVersion`
  moved into `Installer.Common.ps1`; the three near-identical `dotnet
  publish` blocks in `Build-Portable.ps1` driven from a single target table.
- Removed orphaned worker stats endpoints not consumed by the dashboard:
  `topActions` (`top-actions`), `topActionsInFailures`
  (`top-actions-in-failures`), `profileBreakdown` (`profiles`) — query
  builders, route registrations, tests, README.
- Deleted `scripts/Generate-Assets.ps1` and `installer/release-contract.json`
  (zero references anywhere in the repo).

## Decisions recorded

### 1. `truncate` in the dashboard — not deduplicated

Two copies exist, in `api.js` and `rendering.js`, and they are **not
equivalent**: `api.js` truncates by code points (`Array.from(value)`), while
`rendering.js` truncates by UTF-16 code units (`value.slice`). A shared
helper would silently change which copy is authoritative. Decision: leave
both in place; deduplicating would require first deciding that code-point
truncation is correct everywhere, which is a product question, not a
refactor. Revisit if a third copy appears.

### 2. Login route keeps its distinct JSON error body

`readJsonBody.js` returns a parse-failed sentinel that the three ingest
routes convert to a plain-text 400. The `/admin/login` route deliberately
keeps its own JSON 400 (`{ error: ... }`) because the dashboard's login
form reads that shape. Do not fold login into `readJsonBody`'s plain-text
path without updating the login form too.

### 3. Query builders — shared clauses, per-domain structure

The pattern shared by the three `buildFilters` variants is only the
environment clause and the date-range clauses. Everything else differs by
domain (default `topN`/limit values, WHERE vs 1=1 formatting, field names,
which filters exist). Extracting the *whole* builder would have changed
behavior; the shared `filters.js` deliberately stops at the two common
clauses. Any future consolidation must re-verify each domain's defaults and
clause order.

### 4. `Get-ProjectVersion` — single source, per-caller validation

Three copies of the version read existed: `Build-Installer.ps1` (function),
`Build-Portable.ps1` (inline), `release.yml` (inline). Consolidation moved
the *read* (first non-empty `Version` from `Directory.Build.props`) into
`Installer.Common.ps1:Get-ProjectVersion`, but each caller keeps its own
format validation on purpose:

- `Build-Installer.ps1` — SemVer-like (`1.2.3` or `1.2.3-preview`), because
  the installer accepts pre-release suffixes.
- `Build-Portable.ps1` — strictly `^\d+\.\d+\.\d+$` (stable only).
- `release.yml` — strictly `^\d+\.\d+\.\d+$`; the tag/channel logic then
  derives the preview suffix.

Do not collapse these validators: `Build-Installer` intentionally accepts
suffixes the other two reject.

### 5. `-SkipTests` in workflows

`Verify-Safety.ps1` builds and (by default) runs the unit tests. The CI and
release workflows then run `dotnet test --no-build` again. Passing
`-SkipTests` keeps a single test run while preserving the build + safety
scan. `Build-Portable.ps1` also passes it because the release pipeline runs
tests separately; a standalone `Build-Portable` invocation still needs the
full safety check, which is why only the test half is skipped.

## Still open / not done

None outstanding from the plan; final full validation (dotnet build/test
Release, worker `npm test`, dashboard `npm test`, `Verify-Safety.ps1`) is
run after these commits.
