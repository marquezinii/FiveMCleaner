[CmdletBinding()]
param(
    [string]$OutputDirectory = 'artifacts/localization/visual',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$IncludePseudo
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$appPath = Join-Path $workspace "src\Ralven.App\bin\$Configuration\net10.0-windows10.0.19041.0\Ralven.exe"
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw "Build the app before visual validation: $appPath"
}

$config = Get-Content -LiteralPath (Join-Path $workspace 'localization\locales.json') -Raw | ConvertFrom-Json
$languages = @($config.languages | ForEach-Object culture)
if ($IncludePseudo) { $languages += [string]$config.pseudoCulture }
$pages = @(
    'Overview', 'System', 'Applications', 'Games', 'FiveM', 'Pro', 'RalvenAi',
    'Ultra', 'UltraLocked', 'Optimizer', 'FiveMOptimizer', 'History',
    'HistoryPopulated', 'Settings'
)
$outputRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

foreach ($language in $languages) {
    $languageDirectory = Join-Path $outputRoot $language
    New-Item -ItemType Directory -Force -Path $languageDirectory | Out-Null
    foreach ($page in $pages) {
        $capturePath = Join-Path $languageDirectory "$page.png"
        $start = [System.Diagnostics.ProcessStartInfo]::new($appPath)
        $start.WorkingDirectory = Split-Path -Parent $appPath
        $start.UseShellExecute = $false
        foreach ($argument in @(
            '--demo-synthetic',
            "--capture=$capturePath",
            "--capture-page=$page",
            "--capture-language=$language",
            '--capture-size=1440x900'
        )) {
            $start.ArgumentList.Add($argument)
        }

        $process = [System.Diagnostics.Process]::Start($start)
        if ($null -eq $process -or -not $process.WaitForExit(30000)) {
            if ($null -ne $process) { $process.Kill($true) }
            throw "Visual capture timed out: $language/$page"
        }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $capturePath -PathType Leaf)) {
            throw "Visual capture failed: $language/$page (exit $($process.ExitCode))"
        }
    }
}

Write-Host "Localization visual capture passed: $($languages.Count) language(s) x $($pages.Count) page(s) in $outputRoot."
