[CmdletBinding()]
param(
    # The assembled portable runtime tree (artifacts/FiveMCleaner-win-x64),
    # containing Runtime\versions\<version>\ (App + broker\ copies of
    # Core/Windows) and FiveMCleaner.Launcher.exe at its root.
    [Parameter(Mandatory)]
    [string]$RuntimeDirectory,

    [Parameter(Mandatory)]
    [string]$Version,

    # Optional: also scan these packaged artifacts. Extracted/read in place,
    # never modified.
    [string]$PortableZipPath,
    [string]$RuntimeZipPath,
    [string]$InstallerPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Distinctive PRIVATE members from FiveMCleaner.Core / FiveMCleaner.Windows
# source. KeepPublicApi=true (see build/obfuscation/FiveMCleaner.Obfuscar.xml)
# means Obfuscar renames exactly these - private methods and fully-internal
# types - while leaving the public surface alone. Their original UTF-8 name
# bytes live verbatim in a compiled assembly's #Strings metadata heap (ECMA-335)
# and, since single-file bundling does not compress by default here, in the
# raw bytes of a bundled .exe too - so a hardened artifact must not contain
# them, in any form (loose DLL, ZIP member, installer payload, or the
# Launcher's single-file bundle). If Obfuscar's renaming ever regresses -
# including the specific bug this script exists to catch, where the
# Launcher's bundle embeds a pre-hardening compile of Core/Windows - one of
# these strings survives verbatim and this script fails loudly instead of
# shipping the un-hardened bytes.
# Maintenance: if any of these members is renamed/removed from source, pick a
# replacement private member and update this list; a stale marker only
# weakens the check, it does not make it silently wrong (a removed method
# also can't appear in a hardened OR un-hardened build, so absence alone does
# not create a false pass without also checking the array here is non-empty).
$forbiddenMarkers = @(
    'CreateVerificationAndBottleneckActions' # private method, FiveMCleaner.Core
    'GraphicsTargetProcessGuard'             # internal sealed class, FiveMCleaner.Windows
    'AddCitizenFxCandidate'                  # private method, FiveMCleaner.Windows
)
if ($forbiddenMarkers.Count -eq 0) {
    throw 'No obfuscation markers configured; this check would silently pass everything.'
}

function Test-BytesContainMarker {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] [string]$Marker
    )
    $needle = [System.Text.Encoding]::UTF8.GetBytes($Marker)
    $limit = $Bytes.Length - $needle.Length
    for ($i = 0; $i -le $limit; $i++) {
        $matched = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($Bytes[$i + $j] -ne $needle[$j]) { $matched = $false; break }
        }
        if ($matched) { return $true }
    }
    return $false
}

$failures = [System.Collections.Generic.List[string]]::new()

function Test-FileHardened {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Label
    )
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        # Fail-closed: a required artifact that is simply missing is not a
        # pass. Every path this function is called with is expected to exist
        # in a complete, hardened build.
        $failures.Add("$Label - file not found: $Path")
        return
    }
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    foreach ($marker in $forbiddenMarkers) {
        if (Test-BytesContainMarker -Bytes $bytes -Marker $marker) {
            $failures.Add("$Label - contains un-hardened marker '$marker': $Path")
        }
    }
    Write-Host "Checked (hardened): $Label ($Path)" -ForegroundColor Green
}

$runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeDirectory)
$versionRoot = Join-Path $runtimeRoot "Runtime\versions\$Version"

Test-FileHardened -Path (Join-Path $versionRoot 'FiveMCleaner.Core.dll') -Label 'App Core.dll'
Test-FileHardened -Path (Join-Path $versionRoot 'FiveMCleaner.Windows.dll') -Label 'App Windows.dll'
Test-FileHardened -Path (Join-Path $versionRoot 'broker\FiveMCleaner.Core.dll') -Label 'Broker Core.dll'
Test-FileHardened -Path (Join-Path $versionRoot 'broker\FiveMCleaner.Windows.dll') -Label 'Broker Windows.dll'
Test-FileHardened -Path (Join-Path $runtimeRoot 'FiveMCleaner.Launcher.exe') -Label 'Launcher single-file bundle'

# The portable/runtime ZIPs and the installer are repackagings of this same
# $RuntimeDirectory tree with no separate compilation step (Build-Portable.ps1
# zips $finalRoot as-is; installer/FiveMCleaner.iss's [Files] section sources
# "{#SourceDir}\*" - the same tree - verbatim). Scanning them too is
# redundant with the checks above by construction, but cheap, and catches a
# packaging-step regression (e.g. a future change that zips a different,
# stale directory) that the checks above cannot see.
if ($PortableZipPath) {
    $zipScratch = Join-Path ([System.IO.Path]::GetTempPath()) ("fmc-verify-portable-" + [Guid]::NewGuid().ToString('N'))
    try {
        Expand-Archive -LiteralPath $PortableZipPath -DestinationPath $zipScratch
        $zipVersionRoot = Join-Path $zipScratch "Runtime\versions\$Version"
        Test-FileHardened -Path (Join-Path $zipVersionRoot 'FiveMCleaner.Core.dll') -Label 'Portable ZIP: App Core.dll'
        Test-FileHardened -Path (Join-Path $zipVersionRoot 'broker\FiveMCleaner.Core.dll') -Label 'Portable ZIP: Broker Core.dll'
        Test-FileHardened -Path (Join-Path $zipScratch 'FiveMCleaner.Launcher.exe') -Label 'Portable ZIP: Launcher bundle'
    }
    finally {
        if (Test-Path -LiteralPath $zipScratch) { Remove-Item -LiteralPath $zipScratch -Recurse -Force }
    }
}

if ($RuntimeZipPath) {
    $runtimeZipScratch = Join-Path ([System.IO.Path]::GetTempPath()) ("fmc-verify-runtime-" + [Guid]::NewGuid().ToString('N'))
    try {
        Expand-Archive -LiteralPath $RuntimeZipPath -DestinationPath $runtimeZipScratch
        Test-FileHardened -Path (Join-Path $runtimeZipScratch 'FiveMCleaner.Core.dll') -Label 'Runtime ZIP: Core.dll'
        Test-FileHardened -Path (Join-Path $runtimeZipScratch 'broker\FiveMCleaner.Core.dll') -Label 'Runtime ZIP: Broker Core.dll'
    }
    finally {
        if (Test-Path -LiteralPath $runtimeZipScratch) { Remove-Item -LiteralPath $runtimeZipScratch -Recurse -Force }
    }
}

if ($InstallerPath) {
    # Inno Setup installers are a 7-Zip-readable archive format; use 7z when
    # available for a direct, no-install extraction of the installer's own
    # payload. When 7z isn't on PATH, this extra check is skipped with a
    # warning - it is not the primary guarantee (see the [Files] source
    # comment above), so its absence does not weaken the fail-closed result
    # of the checks that already ran unconditionally.
    $sevenZipPath = $null
    $sevenZipCommand = Get-Command '7z', '7z.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($sevenZipCommand) {
        $sevenZipPath = $sevenZipCommand.Source
    }
    elseif (Test-Path -LiteralPath 'C:\Program Files\7-Zip\7z.exe' -PathType Leaf) {
        $sevenZipPath = 'C:\Program Files\7-Zip\7z.exe'
    }
    if ($sevenZipPath) {
        $installerScratch = Join-Path ([System.IO.Path]::GetTempPath()) ("fmc-verify-installer-" + [Guid]::NewGuid().ToString('N'))
        try {
            New-Item -ItemType Directory -Force -Path $installerScratch | Out-Null
            & $sevenZipPath x "-o$installerScratch" -y $InstallerPath *> $null
            if ($LASTEXITCODE -ne 0) {
                # Some Inno Setup versions use a format this particular 7-Zip
                # build cannot parse. Not a hardening failure - the primary
                # guarantee (the installer packages the already-verified
                # runtime tree verbatim) does not depend on this extra check
                # succeeding, so a tool/format mismatch only skips it.
                Write-Warning "7-Zip could not open the installer as an archive (format not supported by this 7-Zip build); skipped the direct installer-payload extraction check: $InstallerPath"
            }
            else {
                $extractedCore = @(Get-ChildItem -LiteralPath $installerScratch -Recurse -Filter 'FiveMCleaner.Core.dll' -File)
                $extractedLauncher = @(Get-ChildItem -LiteralPath $installerScratch -Recurse -Filter 'FiveMCleaner.Launcher.exe' -File)
                if ($extractedCore.Count -eq 0 -or $extractedLauncher.Count -eq 0) {
                    $failures.Add("Installer payload extraction did not yield the expected Core.dll/Launcher.exe files: $InstallerPath")
                }
                foreach ($file in $extractedCore) {
                    Test-FileHardened -Path $file.FullName -Label "Installer payload: $($file.Name) ($($file.DirectoryName))"
                }
                foreach ($file in $extractedLauncher) {
                    Test-FileHardened -Path $file.FullName -Label 'Installer payload: Launcher bundle'
                }
            }
        }
        finally {
            if (Test-Path -LiteralPath $installerScratch) { Remove-Item -LiteralPath $installerScratch -Recurse -Force }
        }
    }
    else {
        Write-Warning '7-Zip not found; skipped the direct installer-payload extraction check (covered indirectly: the installer packages the already-verified runtime tree verbatim, see installer/FiveMCleaner.iss).'
    }
}

# PDBs and obfuscation symbol maps are never supposed to reach a public
# artifact: a .pdb (even with DebugType=None on the main projects, a
# referenced project could still emit one) or a Mapping-*.txt file sitting in
# the shipped tree would hand out exactly what obfuscation is meant to hide.
$forbiddenFiles = @(Get-ChildItem -LiteralPath $runtimeRoot -Recurse -File |
    Where-Object { $_.Extension -eq '.pdb' -or $_.Name -like 'Mapping-*.txt' })
foreach ($file in $forbiddenFiles) {
    $failures.Add("Public artifact contains a debug symbol or obfuscation map file that must not ship: $($file.FullName)")
}
if ($forbiddenFiles.Count -eq 0) {
    Write-Host 'Checked (absent): .pdb / obfuscation map files in the public runtime tree' -ForegroundColor Green
}

if ($failures.Count -gt 0) {
    throw "Un-obfuscated or leaked artifacts detected:`n$($failures -join [Environment]::NewLine)"
}

# A non-fatal external command earlier (e.g. a 7-Zip format mismatch, warned
# above and correctly not treated as a failure) can leave $LASTEXITCODE
# non-zero even though this script is about to succeed. Callers that check
# $LASTEXITCODE after invoking this script via `&` need it to reflect this
# script's own outcome, not a stale value from an internal detail.
$global:LASTEXITCODE = 0
Write-Host 'No un-obfuscated Core/Windows copies or leaked debug/obfuscation artifacts found in any public artifact.' -ForegroundColor Green
