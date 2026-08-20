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
recompiles them again in between, and this ordering is an explicit MSBuild
target-graph guarantee (`AfterTargets`/`BeforeTargets`), not a side effect of
incremental build caching or of which target happens to run first or last.
`src/FiveMCleaner.Launcher/FiveMCleaner.Launcher.csproj` defines two targets,
gated on `-p:FiveMCleanerHarden=true`:

- `HardenBundledAssemblies` (`AfterTargets="ComputeFilesToPublish"`,
  `BeforeTargets="GenerateSingleFileBundle"`) backs up the current
  `Core.dll`/`Windows.dll` bytes, runs `Invoke-Obfuscation.ps1` against
  `FiveMCleaner.Windows`'s own build output in place, then copies the
  hardened `Core.dll` over to `FiveMCleaner.Core`'s own canonical output (a
  separate folder, since `Core` has no reference to `Windows`).
- `RestoreCanonicalAssembliesAfterBundling` (`AfterTargets="GenerateSingleFileBundle"`)
  restores both canonical outputs from that backup, unconditionally, right
  after the bundle has been written.

This backup/restore pair matters because the target above mutates a *shared*
project build output — the same folder every other project reference to
`Core`/`Windows` resolves from. Restoring it turns the hardening into a
transient effect scoped to this one publish, instead of a lasting mutation
that a later `dotnet build`/debug session (or the Broker/App targets, if the
publish order ever changes) could pick up by accident. This was verified by
publishing the Launcher completely in isolation — no Broker/App target
before or after it — with `-p:FiveMCleanerHarden=true`: the resulting bundle
is hardened, and the canonical `Core`/`Windows` build output is back to its
clean, pre-hardening bytes immediately afterward, with nothing else in the
build graph involved.

`scripts/Build-Portable.ps1` passes `-p:FiveMCleanerHarden=true` to every
`-Harden` publish target; it's a no-op for Broker/App, which don't define
these targets.

## Post-obfuscation verification

Three gates run against the hardened output, and none of them trust that the
steps above worked — each one proves it on the actual output bytes:

1. **Structural** (`scripts/Invoke-Obfuscation.ps1`): each rewritten assembly
   must be a valid .NET PE and must differ from its pre-obfuscation bytes, or
   the build fails before anything is hashed or signed. This applies equally
   to Broker's/App's loose copies and to the assemblies the Launcher's
   `HardenBundledAssemblies` target hardens before bundling.
2. **Fail-closed artifact scan** (`scripts/Test-NoUnobfuscatedAssemblies.ps1`):
   run automatically by `Build-Portable.ps1 -Harden` (against the assembled
   runtime tree and both ZIPs) and by `Build-Installer.ps1 -Harden` (against
   the compiled installer). It byte-scans every public artifact — the loose
   App/Broker `Core.dll`/`Windows.dll`, the `Launcher.exe` bundle, both ZIPs'
   contents and (when 7-Zip can parse the installer's format) the installer's
   own extracted payload — for a curated set of **private/internal** member
   names from `Core`/`Windows` source (e.g. `GraphicsTargetProcessGuard`, a
   fully-internal class Obfuscar renames in its entirety). Their original
   UTF-8 name bytes live verbatim in a compiled assembly's `#Strings`
   metadata heap (ECMA-335) and, since single-file bundling isn't compressed
   here, in the raw bytes of a bundled `.exe` too — so their presence
   anywhere in a public artifact means hardening did not apply, and the build
   throws instead of shipping it. The same script also fails the build if any
   `.pdb` or `Mapping-*.txt` (obfuscation symbol map) file is found under the
   public runtime tree — neither is meant to leave the build machine; symbol
   maps are uploaded separately as a private workflow artifact (see below).
3. **Runtime smoke** (`scripts/Test-HardenedRuntime.ps1`): the hardened app is
   launched in `--demo-synthetic --capture` mode and must render its pages and
   exit cleanly. This proves the obfuscated `Core`/`Windows` load and execute —
   renamed members dispatch and encrypted strings decrypt at runtime.

Gate 2 is deliberately structural (metadata identifier names), not a
one-off manual check: it was validated both positively (a correctly hardened
build passes) and negatively (a build with `-Harden` omitted fails loudly,
listing every un-hardened location) before being wired into the pipeline.

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

# Fail-closed scan for un-hardened Core/Windows copies or leaked debug/map files
# (Build-Portable.ps1/Build-Installer.ps1 already run this under -Harden)
.\scripts\Test-NoUnobfuscatedAssemblies.ps1 -RuntimeDirectory .\artifacts\FiveMCleaner-win-x64 -Version <version> `
    -PortableZipPath .\artifacts\FiveMCleaner-win-x64.zip -RuntimeZipPath .\artifacts\FiveMCleaner-Runtime-win-x64.zip
```

The pinned obfuscator (`obfuscar.globaltool`) lives in
`.config/dotnet-tools.json`; `Invoke-Obfuscation.ps1` restores it automatically.
