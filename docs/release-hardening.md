# Release hardening (code obfuscation)

FiveMCleaner is source-available: the clean, readable C# on GitHub stays the
source of truth, and development/CI builds are never obfuscated. Only the
**public release binaries** are hardened, by an obfuscation step that runs
inside the release pipeline. The obfuscator never runs on a user's machine — the
user only ever receives already-hardened binaries.

## Goal and honest scope

Obfuscation here raises the cost of *casual* reverse engineering and binary
patching of a shipped build. It is **not** DRM and does not hide the algorithm
from anyone willing to read the public repository. Any commercially meaningful
decision must still be enforced server-side (see `docs/telemetry.md` and the
Worker), never by a client-side `if`.

## What is obfuscated

Only the internal-logic assemblies:

- `FiveMCleaner.Core` — action catalog, profiles, planning.
- `FiveMCleaner.Windows` — Windows/FiveM adapters and diagnostics.

Everything else is intentionally left untouched, because these assemblies are
resolved **by name at runtime** and renaming their members would break the app
silently:

- `FiveMCleaner.Contracts` — its DTOs and enums are serialized by member name
  across four durable boundaries (broker IPC, broker events, transaction
  journal, local settings) with `UnmappedMemberHandling = Disallow`. Renaming a
  member breaks persisted data and the elevated broker contract.
- `FiveMCleaner.App` — WPF. XAML/BAML binds to view-model members and resolves
  types by string; the obfuscator does not read XAML.
- `FiveMCleaner.Launcher` / `FiveMCleaner.Broker` — entry-point hosts.
- `FiveMCleaner.UpdateRuntime` — the update/rollback state machine is
  safety-critical and low IP value; kept clean deliberately.

### Why `KeepPublicApi` is the correctness guarantee

The obfuscation config (`build/obfuscation/FiveMCleaner.Obfuscar.xml`) sets
`KeepPublicApi=true` + `HidePrivateApi=true`. Because the non-obfuscated
`App`/`Broker` and the JSON layer only ever touch the **public** surface of
`Core`/`Windows`, keeping that surface intact means the app behaves exactly as
built while only private/internal implementation is renamed. `HideStrings=true`
additionally encrypts in-IL string literals (registry paths, WMI queries, log
text).

This invariant is verified during the build: the public type set of each
assembly is byte-identical before and after obfuscation, and the app is only
composed through constructor calls (no reflection-by-name, no DI container
scanning of internal types).

## Where it runs

Obfuscation happens inside `scripts/Build-Portable.ps1` (behind `-Harden`),
right after `dotnet publish` and **before any checksum**. Every downstream
artifact — the runtime/portable ZIPs, the broker `SHA256SUMS.txt`, the release
manifest and the signed update manifest — therefore covers the hardened
binaries. `scripts/Build-Installer.ps1 -Harden` forwards the switch.

The public release workflow (`.github/workflows/release.yml`) always builds with
`-Harden`. Development builds, the dev shortcut and CI test builds do not, so
day-to-day debugging is unaffected.

## Post-obfuscation verification

Two gates run against the hardened output:

1. **Structural** (`scripts/Invoke-Obfuscation.ps1`): each rewritten assembly
   must be a valid .NET PE and must differ from its pre-obfuscation bytes, or
   the build fails before anything is hashed or signed.
2. **Runtime smoke** (`scripts/Test-HardenedRuntime.ps1`): the hardened app is
   launched in `--demo-synthetic --capture` mode and must render its pages and
   exit cleanly. This proves the obfuscated `Core`/`Windows` load and execute —
   renamed members dispatch and encrypted strings decrypt at runtime.

## De-obfuscating crash reports

Obfuscar emits a symbol map per assembly set. The release workflow uploads it as
a private, non-release workflow artifact (`obfuscation-maps-<version>`, 90-day
retention). Use it to translate obfuscated names in a Sentry stack trace back to
the original symbols. The map is never attached to the public release.

## Local usage

```powershell
# Hardened portable runtime
.\scripts\Build-Portable.ps1 -Harden

# Hardened installer (forwards -Harden to the portable build)
.\scripts\Build-Installer.ps1 -Version <version> -Harden

# Smoke a hardened runtime tree
.\scripts\Test-HardenedRuntime.ps1 -RuntimeDirectory .\artifacts\FiveMCleaner-win-x64 -Version <version>
```

The pinned obfuscator (`obfuscar.globaltool`) lives in
`.config/dotnet-tools.json`; `Invoke-Obfuscation.ps1` restores it automatically.

## Known limitation

The `FiveMCleaner.Launcher` is published as a single-file executable that
bundles copies of its referenced assemblies (including `Core`/`Windows`). Those
bundled copies are **not** hardened — only the loose assemblies in
`Runtime\versions\<version>\`, which are what the app actually runs, are. Closing
this gap would require obfuscating the referenced assemblies before the Launcher
is bundled (a build-order change) and is left as a deliberate follow-up.
