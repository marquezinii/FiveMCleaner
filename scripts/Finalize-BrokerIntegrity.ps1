[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string]$Version,

    [switch]$AllowDirtySource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = Join-Path $workspace 'artifacts'
$portableRoot = Join-Path $artifactsRoot 'FiveMCleaner-win-x64'
$versionRoot = Join-Path $portableRoot "Runtime\versions\$Version"
$brokerManifest = Join-Path $versionRoot 'broker-integrity.json'
$runtimeArchive = Join-Path $artifactsRoot 'FiveMCleaner-Runtime-win-x64.zip'
$portableArchive = Join-Path $artifactsRoot 'FiveMCleaner-win-x64.zip'

foreach ($required in @($versionRoot, $brokerManifest)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Signed broker packaging input is missing: $required"
    }
}

$versionPrefix = $versionRoot.TrimEnd('\') + '\'
$checksums = Get-ChildItem -LiteralPath $versionRoot -Recurse -File |
    Where-Object { $_.FullName -ne (Join-Path $versionRoot 'SHA256SUMS.txt') } |
    Sort-Object FullName |
    ForEach-Object {
        if (-not $_.FullName.StartsWith($versionPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to hash a file outside the versioned runtime: $($_.FullName)"
        }
        $relative = $_.FullName.Substring($versionPrefix.Length).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
Set-Content -LiteralPath (Join-Path $versionRoot 'SHA256SUMS.txt') -Value $checksums -Encoding utf8

foreach ($archive in @($runtimeArchive, $portableArchive)) {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
}

Compress-Archive -Path (Join-Path $versionRoot '*') -DestinationPath $runtimeArchive -CompressionLevel Optimal
$runtimeHash = (Get-FileHash -LiteralPath $runtimeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$runtimeArchive.sha256" -Value "$runtimeHash  $([System.IO.Path]::GetFileName($runtimeArchive))" -Encoding ascii

Compress-Archive -Path (Join-Path $portableRoot '*') -DestinationPath $portableArchive -CompressionLevel Optimal
$portableHash = (Get-FileHash -LiteralPath $portableArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$portableArchive.sha256" -Value "$portableHash  $([System.IO.Path]::GetFileName($portableArchive))" -Encoding ascii

& (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
    -Version $Version `
    -SkipPortableBuild `
    -AllowDirtySource:$AllowDirtySource
if ($LASTEXITCODE -ne 0) {
    throw "Rebuilding the installer after broker signing failed with exit code $LASTEXITCODE."
}
