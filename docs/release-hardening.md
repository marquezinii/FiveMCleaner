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
- `FiveMCleaner.Broker` — entry-point host (its own `Core`/`Windows` copy is
  hardened like the App's, see below — the project itself isn't touched).
- `FiveMCleaner.UpdateRuntime` — the update/rollback state machine is
  safety-critical and low IP value; kept clean deliberately.

`FiveMCleaner.Launcher` is a host too, but its `Core`/`Windows` *dependency
copies* need special handling — see "The Launcher's single-file bundle" below.

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

### The Launcher's single-file bundle

`FiveMCleaner.Launcher` publishes as a self-contained single file
(`PublishSingleFile=true`). The .NET SDK's single-file bundler does not read
its managed dependencies from the loose publish output — per
`Microsoft.NET.Publish.targets`, "when publishing to a single file, ... files
are directly written to the bundle file", reading each assembly straight from
its own project's canonical build output path
(`%(ResolvedFileToPublish.Identity)`) at the moment `GenerateSingleFileBundle`
runs. Two consequences that shaped this design:

- Hardening `Core`/`Windows` **after** the Launcher's publish (the way
  Broker/App are hardened above) is too late: the bundle is already written.
- Hardening them **before** a separate `dotnet build`/`publish` invocation
  doesn't stick either: any later build recompiles `Core`/`Windows` from
  source into that same canonical path, discarding the externally-hardened
  bytes regardless of their (newer) timestamp — MSBuild's copy-to-output step
  for referenced assemblies re-derives from its own intermediate (`obj`)
  cache, not from whatever happens to already sit at the output path.

The only point where hardening reliably survives is **inside the Launcher's
own MSBuild execution**, between the moment `ComputeFilesToPublish` fixes
`Core`/`Windows`'s canonical path as the bundle's source and the moment
`GenerateSingleFileBundle` actually reads bytes from that path — nothing
recompiles them again in between. `src/FiveMCleaner.Launcher/FiveMCleaner.Launcher.csproj`
defines a `HardenBundledAssemblies` target (`AfterTargets="ComputeFilesToPublish"`,
`BeforeTargets="GenerateSingleFileBundle"`, gated on `-p:FiveMCleanerHarden=true`)
that runs `Invoke-Obfuscation.ps1` against `FiveMCleaner.Windows`'s own build
output at exactly that point, then copies the hardened `Core.dll` bytes over
to `FiveMCleaner.Core`'s own canonical output (a separate folder, since `Core`
has no reference to `Windows`). `scripts/Build-Portable.ps1` passes that
property to every `-Harden` publish target; it's a no-op for Broker/App, which
don't define the target.

## Post-obfuscation verification

Two gates run against the hardened output:

1. **Structural** (`scripts/Invoke-Obfuscation.ps1`): each rewritten assembly
   must be a valid .NET PE and must differ from its pre-obfuscation bytes, or
   the build fails before anything is hashed or signed. This applies equally
   to Broker's/App's loose copies and to the assemblies the Launcher's
   `HardenBundledAssemblies` target hardens before bundling.
2. **Runtime smoke** (`scripts/Test-HardenedRuntime.ps1`): the hardened app is
   launched in `--demo-synthetic --capture` mode and must render its pages and
   exit cleanly. This proves the obfuscated `Core`/`Windows` load and execute —
   renamed members dispatch and encrypted strings decrypt at runtime.

No public artifact ships an un-hardened `Core`/`Windows` copy: this was
verified by byte-scanning the final `FiveMCleaner.Launcher.exe` (and the
Broker/App loose copies) for literals that only exist in the un-obfuscated
source (e.g. exception messages from `PlanBuilder`/`ProfilePresentationProvider`)
— present in a plain build, absent everywhere once `-Harden` is used.

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
