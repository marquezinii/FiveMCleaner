[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot),
    [string]$Remote = 'origin'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [IO.Path]::GetFullPath($Workspace)

function Invoke-Git {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & git -C $workspace @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git -C '$workspace' $($Arguments -join ' ')"
    }
    return $output
}

$worktreeChanges = @(Invoke-Git -Arguments @('status', '--porcelain=v1', '--untracked-files=all') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($worktreeChanges.Count -gt 0) {
    throw 'Release tag target must have a clean worktree.'
}

Invoke-Git -Arguments @('fetch', '--no-tags', $Remote, "+refs/heads/main:refs/remotes/$Remote/main") | Out-Null
$headCommit = (Invoke-Git -Arguments @('rev-parse', 'HEAD')).Trim()
$mainCommit = (Invoke-Git -Arguments @('rev-parse', "refs/remotes/$Remote/main")).Trim()
if ($headCommit -ne $mainCommit) {
    throw 'Release tag must be created only after the release commit is current origin/main.'
}

[xml]$props = Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props') -Raw
$projectVersion = @($props.Project.PropertyGroup.Version) |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1
if ($projectVersion -ne $Version) {
    throw "Project version '$projectVersion' does not match requested release version '$Version'."
}

$tag = "v$Version"
if (Invoke-Git -Arguments @('ls-remote', '--tags', $Remote, "refs/tags/$tag")) {
    throw "Release tag '$tag' already exists on '$Remote'."
}

Write-Host "Release tag target accepted: $tag -> $headCommit"
