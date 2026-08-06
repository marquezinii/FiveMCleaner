# Shared helpers for the installer build/verify/smoke-test scripts
# (Build-Installer.ps1, Test-Installer.ps1, Build-Portable.ps1). Dot-source
# this file after resolving $workspace and $artifactsRoot in the caller; it
# defines no side effects of its own beyond the functions below.

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

function Assert-UnderArtifacts {
    param([Parameter(Mandatory)][string]$Path)

    Assert-PathUnderRoot -Path $Path -Root $artifactsRoot
}

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$Workspace)

    [xml]$props = Get-Content -LiteralPath (Join-Path $Workspace 'Directory.Build.props') -Raw
    $candidate = @($props.Project.PropertyGroup.Version) |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if (-not $candidate) {
        throw 'Directory.Build.props does not define Version.'
    }

    return [string]$candidate
}
