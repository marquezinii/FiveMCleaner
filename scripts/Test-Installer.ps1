[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [string]$ExpectedVersion,

    [switch]$AllowExistingInstallation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$resolvedInstaller = [System.IO.Path]::GetFullPath($InstallerPath)
$resolvedPublish = [System.IO.Path]::GetFullPath($PublishDirectory)
$smokeId = [Guid]::NewGuid().ToString('N')
$smokeRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot ".installer-smoke-$smokeId"))
$installDirectory = Join-Path $smokeRoot 'app'
$installLog = Join-Path $smokeRoot 'install.log'
$defaultTasksLog = Join-Path $smokeRoot 'default-tasks.log'
$upgradeLog = Join-Path $smokeRoot 'upgrade.log'
$autoUpdateLog = Join-Path $smokeRoot 'auto-update.log'
$uninstallLog = Join-Path $smokeRoot 'uninstall.log'
$uninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{49338651-127F-4FD3-BEAD-88D8C9377672}_is1'
$runRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'FiveMCleaner'
$userDataMarkerRoot = Join-Path $env:LOCALAPPDATA "FiveMCleaner\.installer-smoke-$smokeId"
$userDataMarker = Join-Path $userDataMarkerRoot 'preserve-me.txt'
$installed = $false
$commonSilentArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')

. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

function Get-RegistryValueOrNull {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )

    try {
        return Get-ItemPropertyValue -LiteralPath $Path -Name $Name -ErrorAction Stop
    }
    catch [System.Management.Automation.PSArgumentException] {
        return $null
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return $null
    }
}

function Stop-SmokeAppProcesses {
    param([Parameter(Mandatory)][string]$InstallDirectory)

    $prefix = $InstallDirectory.TrimEnd('\') + '\'
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match 'FiveMCleaner' -and
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
        } |
        ForEach-Object {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
}

Assert-UnderArtifacts $smokeRoot

if (-not $AllowExistingInstallation -and (Test-Path -LiteralPath $uninstallRegistryPath)) {
    throw 'A real FiveMCleaner installation already exists; refusing to replace it during a smoke test.'
}

$existingRunValue = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
if (-not $AllowExistingInstallation -and $null -ne $existingRunValue) {
    throw 'A FiveMCleaner startup entry already exists; refusing to overwrite it during a smoke test.'
}

if ($AllowExistingInstallation) {
    Write-Warning 'Existing FiveMCleaner registration is allowed for this smoke test by explicit operator request.'
}

& (Join-Path $PSScriptRoot 'Verify-Installer.ps1') `
    -InstallerPath $resolvedInstaller `
    -PublishDirectory $resolvedPublish `
    -ExpectedVersion $ExpectedVersion

New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null

try {
    # Defaults: desktop on, startup off. Explicit empty TASKS list would clear both;
    # omit /TASKS so the script defaults apply.
    $defaultTasksArguments = @(
        $commonSilentArguments
        '/CLOSEAPPLICATIONS',
        '/NORESTARTAPPLICATIONS',
        '/NOICONS',
        '/LANG=en',
        "/DIR=$installDirectory",
        "/GROUP=FiveMCleaner Smoke $smokeId",
        "/LOG=$defaultTasksLog"
    )
    $defaultTasksProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $defaultTasksArguments -WindowStyle Hidden -Wait -PassThru
    if ($defaultTasksProcess.ExitCode -ne 0) {
        throw "Silent install with default tasks failed with exit code $($defaultTasksProcess.ExitCode). See $defaultTasksLog"
    }
    $installed = $true

    $startupAfterDefaults = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
    if ($null -ne $startupAfterDefaults) {
        throw 'Startup registry value was created even though the startup task is unchecked by default.'
    }

    $installArguments = @(
        $commonSilentArguments
        '/CLOSEAPPLICATIONS',
        '/NORESTARTAPPLICATIONS',
        '/NOICONS',
        '/LANG=ptbr',
        '/TASKS=desktopicon,startup',
        "/DIR=$installDirectory",
        "/GROUP=FiveMCleaner Smoke $smokeId",
        "/LOG=$installLog"
    )
    $installProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $installArguments -WindowStyle Hidden -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) {
        throw "Silent install failed with exit code $($installProcess.ExitCode). See $installLog"
    }

    $installedExecutable = Join-Path $installDirectory 'FiveMCleaner.Launcher.exe'
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    foreach ($required in @($installedExecutable, $uninstaller)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Installed file not found: $required"
        }
    }

    if (-not (Test-Path -LiteralPath $uninstallRegistryPath)) {
        throw 'Uninstall registry entry was not created.'
    }

    $uninstallRegistration = Get-ItemProperty -LiteralPath $uninstallRegistryPath
    if ($uninstallRegistration.DisplayName -ne 'FiveMCleaner') {
        throw "Unexpected uninstall DisplayName: $($uninstallRegistration.DisplayName)"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and
        $uninstallRegistration.DisplayVersion -ne $ExpectedVersion) {
        throw "Unexpected uninstall DisplayVersion: $($uninstallRegistration.DisplayVersion)"
    }
    $registeredLocation = ([string]$uninstallRegistration.InstallLocation).TrimEnd('\')
    if (-not $registeredLocation.Equals($installDirectory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected uninstall InstallLocation: $registeredLocation"
    }

    $startupValue = Get-ItemPropertyValue -LiteralPath $runRegistryPath -Name $runValueName -ErrorAction Stop
    $expectedStartupValue = '"' + $installedExecutable + '" --startup'
    if ($startupValue -ne $expectedStartupValue) {
        throw "Startup value mismatch. Expected '$expectedStartupValue', got '$startupValue'."
    }

    $publishPrefix = $resolvedPublish.TrimEnd('\') + '\'
    foreach ($sourceFile in Get-ChildItem -LiteralPath $resolvedPublish -Recurse -File) {
        $relative = $sourceFile.FullName.Substring($publishPrefix.Length)
        $installedFile = Join-Path $installDirectory $relative
        if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf)) {
            throw "Installed payload is missing: $relative"
        }

        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $installedHash = (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash
        if ($sourceHash -ne $installedHash) {
            throw "Installed payload hash mismatch: $relative"
        }
    }

    $upgradeArguments = @(
        $commonSilentArguments
        '/CLOSEAPPLICATIONS',
        '/NORESTARTAPPLICATIONS',
        '/NOICONS',
        '/LANG=en',
        '/TASKS=',
        "/DIR=$installDirectory",
        "/GROUP=FiveMCleaner Smoke $smokeId",
        "/LOG=$upgradeLog"
    )
    $upgradeProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $upgradeArguments -WindowStyle Hidden -Wait -PassThru
    if ($upgradeProcess.ExitCode -ne 0) {
        throw "Silent in-place upgrade failed with exit code $($upgradeProcess.ExitCode). See $upgradeLog"
    }

    $startupAfterUpgrade = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
    if ($null -ne $startupAfterUpgrade) {
        throw 'Startup value remains after an upgrade explicitly disabled the startup task.'
    }

    $upgradedExecutableHash = (Get-FileHash -LiteralPath $installedExecutable -Algorithm SHA256).Hash
    $sourceExecutableHash = (Get-FileHash -LiteralPath (Join-Path $resolvedPublish 'FiveMCleaner.Launcher.exe') -Algorithm SHA256).Hash
    if ($upgradedExecutableHash -ne $sourceExecutableHash) {
        throw 'Main executable hash mismatch after in-place upgrade.'
    }

    # Migration/update handoff: exact flags used by UpdateHandoff. The relaunch is
    # nowait, so setup should exit while we stop any app started from the smoke dir.
    $autoUpdateArguments = @(
        $commonSilentArguments
        '/CLOSEAPPLICATIONS',
        '/NORESTARTAPPLICATIONS',
        '/NOCANCEL',
        '/AUTOUPDATE=yes',
        '/NOICONS',
        '/TASKS=',
        "/DIR=$installDirectory",
        "/GROUP=FiveMCleaner Smoke $smokeId",
        "/LOG=$autoUpdateLog"
    )
    $autoUpdateProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $autoUpdateArguments -WindowStyle Hidden -Wait -PassThru
    if ($autoUpdateProcess.ExitCode -ne 0) {
        throw "Silent AUTOUPDATE install failed with exit code $($autoUpdateProcess.ExitCode). See $autoUpdateLog"
    }
    Start-Sleep -Milliseconds 500
    Stop-SmokeAppProcesses -InstallDirectory $installDirectory

    # Simulate the installed app enabling this preference after setup. The
    # uninstaller must still own and remove the product-specific value.
    Set-ItemProperty -LiteralPath $runRegistryPath -Name $runValueName -Value $expectedStartupValue -Type String

    New-Item -ItemType Directory -Force -Path $userDataMarkerRoot | Out-Null
    Set-Content -LiteralPath $userDataMarker -Value "smoke-$smokeId" -Encoding utf8

    $uninstallArguments = @(
        $commonSilentArguments
        "/LOG=$uninstallLog"
    )
    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArguments -WindowStyle Hidden -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Silent uninstall failed with exit code $($uninstallProcess.ExitCode). See $uninstallLog"
    }
    $installed = $false

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ((Test-Path -LiteralPath $installDirectory) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }

    if (Test-Path -LiteralPath $uninstallRegistryPath) {
        throw 'Uninstall registry entry remains after uninstall.'
    }
    $remainingRunValue = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
    if ($null -ne $remainingRunValue) {
        throw 'Startup registry value remains after uninstall.'
    }
    if (Test-Path -LiteralPath $installedExecutable) {
        throw 'Application executable remains after uninstall.'
    }

    if (-not (Test-Path -LiteralPath $userDataMarker -PathType Leaf)) {
        throw 'Silent uninstall removed local user data; it must preserve %LOCALAPPDATA%\FiveMCleaner by default.'
    }

    # Interactive removal choice is still guarded by Verify-Installer.ps1.
    if ((Get-Content -LiteralPath (Join-Path $workspace 'installer\FiveMCleaner.iss') -Raw) -notmatch
        "DelTree\(ExpandConstant\('\{localappdata\}\\FiveMCleaner'\), True, True, True\)") {
        throw 'The explicit interactive removal path for user data is missing.'
    }

    Write-Host 'Installer install/upgrade/uninstall smoke test: OK' -ForegroundColor Green
}
finally {
    Stop-SmokeAppProcesses -InstallDirectory $installDirectory

    if ($installed) {
        $uninstaller = Join-Path $installDirectory 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            $cleanup = Start-Process -FilePath $uninstaller `
                -ArgumentList $commonSilentArguments `
                -WindowStyle Hidden -Wait -PassThru
            if ($cleanup.ExitCode -ne 0) {
                Write-Warning "Cleanup uninstaller exited with $($cleanup.ExitCode)."
            }
        }
    }

    $currentRunValue = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
    if ($null -ne $currentRunValue -and $currentRunValue -like "*$installDirectory*") {
        Remove-ItemProperty -LiteralPath $runRegistryPath -Name $runValueName -Force
    }

    if (Test-Path -LiteralPath $userDataMarkerRoot) {
        Remove-Item -LiteralPath $userDataMarkerRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $smokeRoot) {
        Assert-UnderArtifacts $smokeRoot
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}
