[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('pull_request', 'push', 'workflow_dispatch', 'schedule')]
    [string]$EventName,

    [AllowEmptyCollection()]
    [string[]]$ChangedFiles = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$full = $EventName -in @('workflow_dispatch', 'schedule') -or
    ($EventName -eq 'push' -and $ChangedFiles.Count -eq 0)

function Test-PathScope {
    param([Parameter(Mandatory)][string[]]$Patterns)

    return $full -or [bool]($ChangedFiles | Where-Object {
        $file = $_
        $Patterns | Where-Object { $file -like $_ } | Select-Object -First 1
    })
}

$all = Test-PathScope @('.github/workflows/*', '.github/dependabot.yml')
$node = Test-PathScope @('.node-version')
$dotnet = $all -or (Test-PathScope @(
    'src/*', 'tests/*', 'scripts/*.ps1', 'Ralven.slnx', 'global.json',
    'Directory.Build.*', '.config/dotnet-tools.json'
))
$worker = $all -or $node -or (Test-PathScope @('infra/cloudflare-worker/*'))
$dashboard = $all -or $node -or (Test-PathScope @('infra/dashboard/*'))
$website = $all -or $node -or (Test-PathScope @('website/*'))
$installer = $all -or (Test-PathScope @(
    'installer/*', 'scripts/Build-Installer.ps1', 'scripts/Build-Portable.ps1',
    'scripts/Installer.Common.ps1', 'scripts/Test-Installer.ps1',
    'scripts/Verify-Installer.ps1', 'Directory.Build.props'
))
$dotnet = $dotnet -or $installer
if ($EventName -eq 'schedule') { $installer = $false }

[pscustomobject][ordered]@{
    Dotnet = $dotnet
    Worker = $worker
    Dashboard = $dashboard
    Website = $website
    Installer = $installer
    Sbom = $dotnet -and $EventName -ne 'schedule'
}
