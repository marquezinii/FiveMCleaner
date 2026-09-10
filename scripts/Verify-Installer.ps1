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
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
. (Join-Path $PSScriptRoot 'Installer.Common.ps1')
$installerScript = Join-Path $workspace 'installer\Ralven.iss'

if (-not (Test-Path -LiteralPath $installerScript -PathType Leaf)) {
    throw "Installer script not found: $installerScript"
}

$scriptText = Get-Content -LiteralPath $installerScript -Raw
$requiredPatterns = [ordered]@{
    'public Ralven product name' = '#define AppName "Ralven"'
    'public Ralven installer name' = '#define InstallerBaseName "Ralven-Setup-"'
    'stable AppId'                  = 'AppId=\{#StableAppId\}'
    'Ralven launcher entry point'   = '#define AppExeName "Ralven\.Launcher\.exe"'
    'per-user install'              = 'PrivilegesRequired=lowest'
    'Windows 10 2004 minimum'       = 'MinVersion=10\.0\.19041'
    'x64-compatible runtime gate'   = 'ArchitecturesAllowed=x64compatible'
    'modern system-aware theme'     = 'WizardStyle=modern dynamic'
    'official application icon'     = 'SetupIconFile=.*Ralven\.ico'
    'stable shortcut app identity'  = '#define AppUserModelId "Ralven\.Ralven"[\s\S]*AppUserModelID: "\{#AppUserModelId\}"'
    'proportional wizard artwork'   = 'WizardImageFile=\{#InstallerArtworkPath\}'
    'dark wizard artwork'           = 'WizardImageFileDynamicDark=\{#InstallerArtworkPathDark\}'
    'comfortable wizard size'       = 'WizardSizePercent=140,135'
    'formatted complete license'    = 'LicenseFile=\{#InstallerLicensePath\}'
    'formatted localized info'      = 'InfoBeforeFile: "\{#InstallerInfo'
    'real native progress'          = 'procedure CurInstallProgressChanged\(CurProgress, MaxProgress: Integer\)'
    'details hidden by default'     = 'TechnicalDetailsVisible := False'
    'optional live details'         = 'TechnicalDetailsButton\.Caption := CustomMessage\(''ShowDetails''\)'
    'wrapped live details'          = 'TechnicalDetailsMemo\.WordWrap := True'
    'native activity details'       = 'NativeStatus := WizardForm\.StatusLabel\.Caption'
    'native file details'           = 'NativeFilename := WizardForm\.FilenameLabel\.Caption'
    'real shortcut stage boundary'  = 'BeforeInstall: BeginShellConfiguration'
    'mnemonic stripping fallback'   = "StringChangeEx\(Result, '&', '', True\)"
    'ultra lzma compression'        = 'Compression=lzma2/ultra'
    'localized finished label'      = '(?im)^\s*en\.FinishedLabel='
    'Spanish installer language'    = '(?im)^Name: "es"; MessagesFile: "compiler:Languages\\Spanish\.isl"'
    'French installer language'     = '(?im)^Name: "fr"; MessagesFile: "compiler:Languages\\French\.isl"'
    'Spanish custom messages'       = '(?im)^es\.RemoveUserDataQuestion='
    'French custom messages'        = '(?im)^fr\.RemoveUserDataQuestion='
    'localized uninstall shortcut'  = 'Name: "\{group\}\\\{cm:UninstallShortcut\}"'
    'english app comments metadata' = 'AppComments=Transparent and reversible Windows management for diagnostics, maintenance and optimization\.'
    'Windows language detection'    = 'LanguageDetectionMethod=uilanguage'
    'fresh language detection'      = 'UsePreviousLanguage=no'
    'offline embedded payload'      = 'Source: "\{#SourceDir\}\\\*"'
    'payload timestamps normalized' = 'Flags: .*notimestamp'
    'safe close through RM'         = 'CloseApplications=yes'
    'no automatic app restart'      = 'RestartApplications=no'
    'no automatic reboot after run' = 'RestartIfNeededByRun=no'
    'concurrent setup guard'        = 'SetupMutex=Ralven\.Setup\.'
    'desktop shortcut enabled by default' = 'Name: "desktopicon"; Description: "\{cm:DesktopIcon\}"; GroupDescription:'
    'startup enabled by default'    = '(?m)^Name: "startup"; Description: "\{cm:StartWithWindowsTask\}"; GroupDescription: "\{cm:AdditionalShortcuts\}:"\s*$'
    'startup ownership cleanup'     = 'ValueName: "Ralven"; Flags: deletevalue uninsdeletevalue; Tasks: not startup'
    'no launch in silent installs'  = 'Flags: nowait postinstall skipifsilent'
    'auto-update relaunch gated'    = 'Check: IsAutomaticUpdateRelaunch'
    'auto-update needs explicit opt-in' = "WizardSilent and[\s\S]*\{param:AUTOUPDATE\|no\}"
    'auto-update relaunch is the app' = 'Filename: "\{app\}\\\{#AppExeName\}"; Parameters: "--updated='
    'redirection guard'             = 'RedirectionGuard=yes'
    'explicit user-data removal'    = 'RemoveUserDataQuestion='
    'silent uninstall preserves data' = 'SuppressibleMsgBox\([\s\S]*IDNO\) = IDYES'
    'fixed user-data directory'     = "DelTree\(ExpandConstant\('\{localappdata\}\\Ralven'\), True, True, True\)"
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
    'unchecked desktop shortcut' = 'Name: "desktopicon";.*Flags: unchecked'
    'unchecked startup' = 'Name: "startup";.*Flags: unchecked'
    'mnemonics in native captions' = '(?im)^\s*(?:en|ptbr)\.(?:ButtonBack|ButtonNext|ButtonInstall|ButtonFinish|ButtonBrowse|ButtonWizardBrowse|ButtonNewFolder|ButtonYes|ButtonNo|LicenseAccepted|LicenseNotAccepted)=.*&'
    'mnemonics in task captions' = '(?im)^\s*(?:en|ptbr)\.(?:AdditionalShortcuts|DesktopIcon|StartWithWindowsTask|LaunchProgram|UninstallShortcut)=.*&'
}

foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($scriptText -match $entry.Value) {
        throw "Forbidden installer behavior detected: $($entry.Key)."
    }
}

$deleteStatements = @([regex]::Matches($scriptText, '(?im)^\s*DelTree\s*\([^\r\n]+\);'))
$expectedDeleteStatement = "DelTree(ExpandConstant('{localappdata}\Ralven'), True, True, True);"
if ($deleteStatements.Count -ne 1 -or
    $deleteStatements[0].Value.Trim() -ne $expectedDeleteStatement) {
    throw 'Installer script contains an unapproved local data deletion.'
}

foreach ($infoRelative in @(
    'installer\install-info.en.txt',
    'installer\install-info.pt-BR.txt',
    'installer\install-info.es.txt',
    'installer\install-info.fr.txt'
)) {
    $infoPath = Join-Path $workspace $infoRelative
    if (-not (Test-Path -LiteralPath $infoPath -PathType Leaf)) {
        throw "Installer info file missing: $infoRelative"
    }
    $infoText = Get-Content -LiteralPath $infoPath -Raw
    foreach ($needle in @(
        'LOCALAPPDATA',
        'sha256',
        'vemryx.com/Ralven'
    )) {
        if ($infoText -notmatch [regex]::Escape($needle)) {
            throw "Installer info contract missing '$needle' in $infoRelative."
        }
    }
}

$documentProbe = Join-Path $artifactsRoot ('.installer-documents-verify-' + [Guid]::NewGuid().ToString('N'))
Assert-UnderArtifacts -Path $documentProbe
try {
    & (Join-Path $PSScriptRoot 'New-InstallerDocuments.ps1') `
        -LicensePath (Join-Path $workspace 'LICENSE') `
        -EnglishInfoPath (Join-Path $workspace 'installer\install-info.en.txt') `
        -PortugueseInfoPath (Join-Path $workspace 'installer\install-info.pt-BR.txt') `
        -SpanishInfoPath (Join-Path $workspace 'installer\install-info.es.txt') `
        -FrenchInfoPath (Join-Path $workspace 'installer\install-info.fr.txt') `
        -OutputDirectory $documentProbe

    foreach ($documentName in @('license.rtf', 'install-info.en.rtf', 'install-info.pt-BR.rtf', 'install-info.es.rtf', 'install-info.fr.rtf')) {
        $documentPath = Join-Path $documentProbe $documentName
        $rtf = Get-Content -LiteralPath $documentPath -Raw
        if (-not $rtf.StartsWith('{\rtf1', [StringComparison]::Ordinal)) {
            throw "Installer document is not valid RTF: $documentName"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $documentProbe) {
        Remove-Item -LiteralPath $documentProbe -Recurse -Force
    }
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
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
        throw 'ExpectedVersion is required for the versioned runtime payload.'
    }
    $versionRoot = "Runtime\versions\$ExpectedVersion"
    foreach ($requiredFile in @(
        'Ralven.Launcher.exe',
        'Runtime\active.json',
        "$versionRoot\Ralven.exe",
        "$versionRoot\Ralven.runtimeconfig.json",
        "$versionRoot\Sentry.dll",
        "$versionRoot\Config\appsettings.Production.json",
        "$versionRoot\coreclr.dll",
        "$versionRoot\hostfxr.dll",
        "$versionRoot\broker\Ralven.Broker.exe",
        "$versionRoot\broker\Ralven.Broker.runtimeconfig.json"
    )) {
        $candidate = Join-Path $resolvedPublish $requiredFile
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Self-contained payload is missing: $requiredFile"
        }
    }

    $runtimeConfig = Get-Content -LiteralPath (Join-Path $resolvedPublish "$versionRoot\Ralven.runtimeconfig.json") -Raw | ConvertFrom-Json
    if (-not $runtimeConfig.runtimeOptions.includedFrameworks -or
        @($runtimeConfig.runtimeOptions.includedFrameworks).Count -lt 2) {
        throw 'Runtime config does not prove a self-contained Windows Desktop publish.'
    }

    $productionConfig = Get-Content `
        -LiteralPath (Join-Path $resolvedPublish "$versionRoot\Config\appsettings.Production.json") `
        -Raw | ConvertFrom-Json
    if ($productionConfig.environment -ne 'Production') {
        throw 'Release payload diagnostics environment is not Production.'
    }
    if ($productionConfig.telemetryEndpoint -ne
        'https://api.vemryx.com/telemetry') {
        throw 'Release payload telemetry endpoint is not the allowlisted production endpoint.'
    }
    $sentryDsn = $null
    if (-not [Uri]::TryCreate($productionConfig.sentryDsn, [UriKind]::Absolute, [ref]$sentryDsn) -or
        $sentryDsn.Scheme -ne [Uri]::UriSchemeHttps -or
        -not $sentryDsn.Host.EndsWith('.sentry.io', [StringComparison]::OrdinalIgnoreCase) -or
        [string]::IsNullOrWhiteSpace($sentryDsn.UserInfo) -or
        -not ($sentryDsn.Segments[-1].Trim('/') -as [long])) {
        throw 'Release payload Sentry DSN is invalid.'
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
