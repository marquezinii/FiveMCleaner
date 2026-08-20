[CmdletBinding()]
param(
    # Directory holding the freshly published, loose assemblies to harden in
    # place (e.g. the staged app or broker publish output). Only the internal
    # logic assemblies present here are obfuscated; every other file is left
    # untouched.
    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    # Optional directory to collect the Obfuscar symbol map (Mapping.txt) so an
    # obfuscated crash report can be de-obfuscated later. One map per assembly
    # set is copied, named after the source directory.
    [string]$MappingOutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
$workspace = Split-Path -Parent $scriptRoot
$template = Join-Path $workspace 'build\obfuscation\FiveMCleaner.Obfuscar.xml'

if (-not (Test-Path -LiteralPath $template -PathType Leaf)) {
    throw "Obfuscar project template not found: $template"
}

$inPath = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $inPath -PathType Container)) {
    throw "Publish directory to harden not found: $inPath"
}

# Assemblies the template declares as <Module>. Skip the run entirely if none
# of them are present (e.g. the broker output has no UpdateRuntime); never fail
# just because a directory legitimately lacks one.
$targetAssemblies = @('FiveMCleaner.Core.dll', 'FiveMCleaner.Windows.dll')
$present = @($targetAssemblies | Where-Object { Test-Path -LiteralPath (Join-Path $inPath $_) -PathType Leaf })
if ($present.Count -eq 0) {
    Write-Host "No hardenable assemblies in $inPath; nothing to obfuscate." -ForegroundColor Yellow
    return
}
if ($present.Count -ne $targetAssemblies.Count) {
    throw "Expected all of [$($targetAssemblies -join ', ')] in $inPath but only found [$($present -join ', ')]. Refusing to ship a partially hardened runtime."
}

# Record the pre-obfuscation hashes so we can prove the step actually rewrote
# the shipped assemblies rather than silently passing through.
$before = @{}
foreach ($assembly in $targetAssemblies) {
    $before[$assembly] = (Get-FileHash -LiteralPath (Join-Path $inPath $assembly) -Algorithm SHA256).Hash
}

$runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("fmc-obfuscar-" + [Guid]::NewGuid().ToString('N'))
$outPath = Join-Path $runRoot 'out'
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
$project = Join-Path $runRoot 'project.xml'

try {
    # Obfuscar reads InPath/OutPath from the project XML; substitute the concrete
    # per-run paths into the reviewed template rather than hand-building config.
    (Get-Content -LiteralPath $template -Raw).
        Replace('__INPATH__', $inPath).
        Replace('__OUTPATH__', $outPath) |
        Set-Content -LiteralPath $project -Encoding utf8

    Push-Location $workspace
    try {
        # Idempotent; restores the pinned Obfuscar from .config/dotnet-tools.json
        # so the step works locally and in CI without a separate manual restore.
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) {
            throw 'Restoring the pinned .NET tools (Obfuscar) failed.'
        }

        dotnet tool run obfuscar.console -- $project
        if ($LASTEXITCODE -ne 0) {
            throw "Obfuscar failed for $inPath (exit code $LASTEXITCODE)."
        }
    }
    finally {
        Pop-Location
    }

    foreach ($assembly in $targetAssemblies) {
        $obfuscated = Join-Path $outPath $assembly
        if (-not (Test-Path -LiteralPath $obfuscated -PathType Leaf)) {
            throw "Obfuscar did not emit $assembly for $inPath."
        }

        # Structural gate: a corrupt PE throws BadImageFormatException here,
        # before the broken assembly is ever hashed, signed or shipped. Reads
        # the manifest without loading the assembly into this process.
        try {
            [void][System.Reflection.AssemblyName]::GetAssemblyName($obfuscated)
        }
        catch {
            throw "Obfuscated $assembly is not a valid .NET assembly: $($_.Exception.Message)"
        }

        Copy-Item -LiteralPath $obfuscated -Destination (Join-Path $inPath $assembly) -Force

        $after = (Get-FileHash -LiteralPath (Join-Path $inPath $assembly) -Algorithm SHA256).Hash
        if ($after -eq $before[$assembly]) {
            throw "Obfuscation left $assembly byte-identical; the hardening step is not taking effect."
        }
        Write-Host "Hardened $assembly in $inPath" -ForegroundColor Green
    }

    if (-not [string]::IsNullOrWhiteSpace($MappingOutputDirectory)) {
        $mapping = Join-Path $outPath 'Mapping.txt'
        if (Test-Path -LiteralPath $mapping -PathType Leaf) {
            New-Item -ItemType Directory -Force -Path $MappingOutputDirectory | Out-Null
            $label = (Split-Path -Leaf $inPath)
            Copy-Item -LiteralPath $mapping -Destination (Join-Path $MappingOutputDirectory "Mapping-$label.txt") -Force
        }
    }
}
finally {
    if (Test-Path -LiteralPath $runRoot) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
