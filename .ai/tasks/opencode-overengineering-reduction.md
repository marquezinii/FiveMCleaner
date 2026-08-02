# Overengineering reduction — opencode

- Agent: opencode
- Branch/worktree: `ai/opencode/overengineering-reduction`
- Objective: careful dead-code / over-engineering reduction without behavior change
- Status: **pronto para integração**

## Summary

Removed verified dead surfaces only. No product behavior changes intended.

### App / Core / Windows / Broker / UpdateRuntime

- `MainViewModel`: dropped unused streaming-protection UI surface, profile presentation Risks/Reversibility/Variability, and unbound `GpuName`/`ArchitectureLabel` VM props.
- RESX (en/pt-BR/es): removed orphaned `Streaming.*` protection keys and unused `Profiles.Presentation.*` Risks/Reversibility/Variability/Label keys. Kept live Benefits + Impact keys.
- `GtaVCommandLineFile.ComputeSha256(IReadOnlyList<string>)` + unused `using System.Security.Cryptography`.
- `WindowsActionCatalog.TryGet` (all callers use `GetRequired`).
- `UpdaterDiagnostics`: constructor no longer takes injectable endpoint; always uses `UpdaterEventsEndpoint`. Call sites + test updated.
- `ThemeManager.IsLightTheme` (never read).
- `LocalizationService.SupportedLanguages` + `AppLanguageOption` (UI uses radio bindings, not the list).
- `LocalTelemetryQueue.PurgeOlderThan` + its sole test (production uses `Prune`).
- Broker: extracted shared `PublishTimeout` helper for the two identical timeout failure paths.

### Website (template scaffolding only; app kept live)

- Deleted: `app/chatgpt-auth.ts`, `db/`, `examples/`, `drizzle/`, `drizzle.config.ts`, template `README.md`.
- `package.json`: removed `drizzle-orm`, `drizzle-kit`, `db:generate`.
- `worker/index.ts`: removed unused `/_vinext/image` optimizer path (all `<Image>` use `unoptimized`).
- `build/sites-vite-plugin.ts`: stopped copying non-existent drizzle migrations.

## Validation

| Check | Result |
| --- | --- |
| `dotnet build FiveMCleaner.slnx -c Release` | 0 errors / 0 warnings |
| `dotnet test` Release | 616 passed |
| `scripts/Verify-Safety.ps1` | passed |
| `website` npm lint + `tsc --noEmit` + `npm test` | passed (3 website tests) |

## Decisions / left alone

- Security hardening, retries, signatures, file-lock rollback, PBKDF2, CORS, CSV injection: out of scope.
- `website/` Next app itself is live — only unused starter scaffolding removed.
- `ActionReversibility.FullyReversible` enum values stay (live catalog metadata).
- D1 binding still present in `website/.openai/hosting.json` / vite config (infrastructure leftover; no app code consumes it). Safe to revisit later.
- Did not touch other worktrees/branches.

## Commits

- Single local commit on this branch (see git log).
