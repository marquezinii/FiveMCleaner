[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $workspace 'src\FiveMCleaner.App\FiveMCleaner.App.csproj'
$iconPath = Join-Path $workspace 'src\FiveMCleaner.App\Assets\FiveMCleaner.ico'
$launcherPath = Join-Path $workspace 'scripts\Start-DevelopmentApp.ps1'

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "FiveMCleaner.App.csproj was not found under the expected workspace: $workspace"
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding utf8
$propertyGroups = @($project.Project.PropertyGroup)
$targetFramework = $propertyGroups.TargetFramework |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
$assemblyName = $propertyGroups.AssemblyName |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
$outputType = $propertyGroups.OutputType |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($targetFramework) -or
    [string]::IsNullOrWhiteSpace($assemblyName)) {
    throw 'The app project does not declare TargetFramework and AssemblyName.'
}

if ($outputType -ne 'WinExe') {
    throw "The development shortcut requires a Windows GUI executable, but OutputType is '$outputType'."
}

if (-not (Test-Path -LiteralPath $launcherPath -PathType Leaf)) {
    throw "The development launcher was not found: $launcherPath"
}

foreach ($requiredFile in @($iconPath, $launcherPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required development file was not found: $requiredFile"
    }
}

if ($Build -and $PSCmdlet.ShouldProcess($launcherPath, 'Build the current Release development application')) {
    & $launcherPath -NoLaunch
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
    -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
    throw 'Windows did not return a valid Desktop directory.'
}

$shortcutPath = Join-Path $desktopDirectory 'FiveMCleaner - Desenvolvimento.lnk'
if (-not $PSCmdlet.ShouldProcess($shortcutPath, 'Create or update the real development shortcut')) {
    Write-Host "Target: $launcherPath"
    return
}

$legacyShortcutPath = Join-Path $desktopDirectory 'FiveMCleaner - Simulacao.lnk'
if (Test-Path -LiteralPath $legacyShortcutPath -PathType Leaf) {
    $shortcutBackupDirectory = Join-Path $workspace 'artifacts\desktop-shortcut-backup'
    New-Item -ItemType Directory -Path $shortcutBackupDirectory -Force | Out-Null
    $legacyBackupPath = Join-Path $shortcutBackupDirectory 'FiveMCleaner - Simulacao.lnk'
    Move-Item -LiteralPath $legacyShortcutPath -Destination $legacyBackupPath -Force
}

$powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not (Test-Path -LiteralPath $powershellPath -PathType Leaf)) {
    throw "Windows PowerShell was not found at the expected location: $powershellPath"
}

$shell = $null
$shortcut = $null
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $powershellPath
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$launcherPath`""
    $shortcut.WorkingDirectory = $workspace
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.Description = 'FiveMCleaner - desenvolvimento local com build Release atualizado'
    $shortcut.WindowStyle = 7
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    }

    if ($null -ne $shell) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

Write-Host "Development shortcut ready: $shortcutPath" -ForegroundColor Green
Write-Host "Target: $launcherPath"
