# Shared helpers for the installer build/verify/smoke-test scripts
# (Build-Installer.ps1, Test-Installer.ps1). Dot-source this file after
# resolving $artifactsRoot in the caller; it defines no side effects of its
# own beyond the functions below.

Set-StrictMode -Version Latest

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = $Root.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside artifacts: $resolved"
    }
}
