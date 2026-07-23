[CmdletBinding()]
param(
    [string]$InstallerPath,
    [string]$PublishDirectory,
    [string]$ExpectedVersion,
    [switch]$ScriptOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$installerScript = Join-Path $workspace 'installer\FiveMCleaner.iss'

if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "Installer script not found: $installerScript"
}

$scriptText = Get-Content -LiteralPath $installerScript -Raw
$requiredPatterns = [ordered]@{
    'stable AppId'                  = 'AppId=\{#StableAppId\}'
    'per-user install'              = 'PrivilegesRequired=lowest'
    'Windows 10 2004 minimum'       = 'MinVersion=10\.0\.19041'
    'x64-compatible runtime gate'   = 'ArchitecturesAllowed=x64compatible'
    'modern system-aware theme'     = 'WizardStyle=modern dynamic'
    'official application icon'     = 'SetupIconFile=.*FiveMCleaner\.ico'
    'proportional wizard artwork'   = 'WizardImageFile=\{#InstallerArtworkPath\}'
    'Windows language detection'    = 'LanguageDetectionMethod=uilanguage'
    'fresh language detection'      = 'UsePreviousLanguage=no'
    'offline embedded payload'      = 'Source: "\{#SourceDir\}\\\*"'
    'payload timestamps normalized' = 'Flags: .*notimestamp'
    'safe close through RM'         = 'CloseApplications=yes'
    'no automatic app restart'      = 'RestartApplications=no'
    'concurrent setup guard'        = 'SetupMutex=FiveMCleaner\.Setup\.'
    'optional desktop shortcut'     = 'Name: "desktopicon";.*Flags: unchecked'
    'optional startup'              = 'Name: "startup";.*Flags: unchecked'
    'startup ownership cleanup'     = 'ValueName: "FiveMCleaner"; Flags: deletevalue uninsdeletevalue; Tasks: not startup'
    'no launch in silent installs'  = 'Flags: nowait postinstall skipifsilent'
    'redirection guard'             = 'RedirectionGuard=yes'
    'explicit user-data removal'    = 'RemoveUserDataQuestion='
    'silent uninstall preserves data' = 'SuppressibleMsgBox\([\s\S]*IDNO\) = IDYES'
    'fixed user-data directory'     = "DelTree\(ExpandConstant\('\{localappdata\}\\FiveMCleaner'\), True, True, True\)"
}

foreach ($entry in $requiredPatterns.GetEnumerator()) {
    if ($scriptText -notmatch $entry.Value) {
        throw "Installer contract missing: $($entry.Key)."
    }
}

$forbiddenPatterns = [ordered]@{
    'PowerShell execution'    = '(?im)^\s*Filename\s*:.*powershell'
    'Command Prompt execution'= '(?im)^\s*Filename\s*:.*cmd\.exe'
    'remote payload download' = '(?im)^\s*Source\s*:.*https?://'
    'elevated installer'      = '(?im)^\s*PrivilegesRequired\s*=\s*admin'
    'forced process closing'  = '(?im)^\s*CloseApplications\s*=\s*force'
    'forced reboot'           = '(?im)^\s*AlwaysRestart\s*=\s*yes'
    'shell execution helper'  = '(?im)\b(ShellExec|Exec|CreateProcess)\s*\('
    'broad install deletion'  = '(?im)^\s*Type\s*:\s*filesandordirs\b'
}

foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($scriptText -match $entry.Value) {
        throw "Forbidden installer behavior detected: $($entry.Key)."
    }
}

$deleteStatements = @([regex]::Matches($scriptText, '(?im)^\s*DelTree\s*\([^\r\n]+\);'))
$expectedDeleteStatement = "DelTree(ExpandConstant('{localappdata}\FiveMCleaner'), True, True, True);"
if ($deleteStatements.Count -ne 1 -or
    $deleteStatements[0].Value.Trim() -ne $expectedDeleteStatement) {
    throw 'Installer script contains an unapproved local data deletion.'
}

Write-Host 'Installer source contract: OK' -ForegroundColor Green

if ($ScriptOnly) {
    return
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    throw 'InstallerPath is required unless -ScriptOnly is used.'
}

$resolvedInstaller = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $resolvedInstaller -PathType Leaf)) {
    throw "Installer not found: $resolvedInstaller"
}

$installerInfo = Get-Item -LiteralPath $resolvedInstaller
if ($installerInfo.Length -lt 1MB) {
    throw "Installer is unexpectedly small: $($installerInfo.Length) bytes."
}

$header = [System.IO.File]::ReadAllBytes($resolvedInstaller)[0..1]
if ($header[0] -ne 0x4D -or $header[1] -ne 0x5A) {
    throw 'Installer is not a Windows PE executable.'
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and
    $installerInfo.Name -notlike "*-$ExpectedVersion-win-x64.exe") {
    throw "Installer filename does not contain expected version '$ExpectedVersion'."
}

$sidecarPath = "$resolvedInstaller.sha256"
if (Test-Path -LiteralPath $sidecarPath -PathType Leaf) {
    $expectedHash = ((Get-Content -LiteralPath $sidecarPath -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
    $actualHash = (Get-FileHash -LiteralPath $resolvedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash -ne $actualHash) {
        throw 'Installer SHA-256 sidecar does not match the executable.'
    }
}

$signature = Get-AuthenticodeSignature -LiteralPath $resolvedInstaller
if ($signature.Status -notin @('NotSigned', 'Valid')) {
    throw "Unexpected Authenticode status for installer: $($signature.Status)."
}

if (-not [string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $resolvedPublish = [System.IO.Path]::GetFullPath($PublishDirectory)
    foreach ($requiredFile in @(
        'FiveMCleaner.exe',
        'FiveMCleaner.runtimeconfig.json',
        'coreclr.dll',
        'hostfxr.dll',
        'broker\FiveMCleaner.Broker.exe',
        'broker\FiveMCleaner.Broker.runtimeconfig.json'
    )) {
        $candidate = Join-Path $resolvedPublish $requiredFile
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Self-contained payload is missing: $requiredFile"
        }
    }

    $runtimeConfig = Get-Content -LiteralPath (Join-Path $resolvedPublish 'FiveMCleaner.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtimeConfig.runtimeOptions.includedFrameworks -or
        @($runtimeConfig.runtimeOptions.includedFrameworks).Count -lt 2) {
        throw 'Runtime config does not prove a self-contained Windows Desktop publish.'
    }

    $debugFiles = @(Get-ChildItem -LiteralPath $resolvedPublish -Recurse -File |
        Where-Object { $_.Extension -eq '.pdb' })
    if ($debugFiles.Count -ne 0) {
        throw 'Release payload contains debug symbols.'
    }

    $scriptExtensions = @('.bat', '.cmd', '.ps1', '.vbs', '.wsf')
    $installedScripts = @(Get-ChildItem -LiteralPath $resolvedPublish -Recurse -File |
        Where-Object { $_.Extension -in $scriptExtensions })
    if ($installedScripts.Count -ne 0) {
        throw 'Release payload contains a shell or script file.'
    }

    $reparsePoints = @(Get-ChildItem -LiteralPath $resolvedPublish -Recurse -Force |
        Where-Object { ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0 })
    if ($reparsePoints.Count -ne 0) {
        throw 'Release payload contains a reparse point.'
    }
}

Write-Host "Installer artifact contract: OK ($($signature.Status))" -ForegroundColor Green
