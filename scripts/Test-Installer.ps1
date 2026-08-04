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

    $prefix = $InstallDirectory.TrimEnd('\')
    $names = @('FiveMCleaner.exe', 'FiveMCleaner.Launcher.exe', 'FiveMCleaner.Broker.exe')
    foreach ($name in $names) {
        $filter = "Name = '$name'"
        Get-CimInstance -ClassName Win32_Process -Filter $filter -ErrorAction SilentlyContinue |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                $_.ExecutablePath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
            } |
            ForEach-Object {
                # taskkill is more reliable than Stop-Process for trees started by Inno [Run].
                & taskkill.exe /PID $_.ProcessId /T /F 2>$null | Out-Null
            }
    }
}

function Remove-SmokeDesktopShortcut {
    param([Parameter(Mandatory)][string]$InstallDirectory)

    $prefix = $InstallDirectory.TrimEnd('\')
    $candidates = @(
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'FiveMCleaner.lnk'),
        (Join-Path $env:USERPROFILE 'OneDrive\Desktop\FiveMCleaner.lnk'),
        (Join-Path $env:USERPROFILE 'Desktop\FiveMCleaner.lnk')
    ) | Select-Object -Unique

    $shell = $null
    try {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($lnk in $candidates) {
            if (-not (Test-Path -LiteralPath $lnk -PathType Leaf)) {
                continue
            }
            $target = $shell.CreateShortcut($lnk).TargetPath
            if (-not [string]::IsNullOrWhiteSpace($target) -and
                $target.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue
            }
        }
    }
    finally {
        if ($null -ne $shell) {
            [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
        }
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
    # Product defaults: desktop on, startup off (Flags: unchecked on startup).
    # Always pass an explicit /TASKS list: with UsePreviousTasks=yes an older
    # install (or -AllowExistingInstallation) would otherwise restore startup.
    $defaultTasksArguments = @(
        $commonSilentArguments
        '/CLOSEAPPLICATIONS',
        '/NORESTARTAPPLICATIONS',
        '/NOICONS',
        '/LANG=en',
        '/TASKS=desktopicon',
        "/DIR=$installDirectory",
        "/GROUP=FiveMCleaner Smoke $smokeId",
        "/LOG=$defaultTasksLog"
    )
    Write-Host '1/6 Install with desktopicon only...' -ForegroundColor Cyan
    $defaultTasksProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $defaultTasksArguments -WindowStyle Hidden -Wait -PassThru
    if ($defaultTasksProcess.ExitCode -ne 0) {
        throw "Silent install with default tasks failed with exit code $($defaultTasksProcess.ExitCode). See $defaultTasksLog"
    }
    $installed = $true

    $startupAfterDefaults = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
    if ($null -ne $startupAfterDefaults) {
        throw 'Startup registry value was created when only the desktopicon task was selected.'
    }

    Write-Host '2/6 Upgrade with desktopicon+startup...' -ForegroundColor Cyan
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

    Write-Host '3/6 Verifying installed payload hashes...' -ForegroundColor Cyan
    $publishPrefix = $resolvedPublish.TrimEnd('\') + '\'
    $payloadFiles = @(Get-ChildItem -LiteralPath $resolvedPublish -Recurse -File)
    $checked = 0
    foreach ($sourceFile in $payloadFiles) {
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
        $checked++
    }
    Write-Host "Payload files verified: $checked" -ForegroundColor Cyan

    Write-Host '4/6 Upgrade clearing tasks...' -ForegroundColor Cyan
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

    Write-Host '5/6 AUTOUPDATE contract (no app launch)...' -ForegroundColor Cyan
    # Do not run /AUTOUPDATE=yes here: it relaunches FiveMCleaner.exe and is
    # covered by Verify-Installer.ps1 + UpdateHandoff unit tests. Live relaunch
    # leaves a GUI process that blocks uninstall and pollutes the operator machine.
    $issText = Get-Content -LiteralPath (Join-Path $workspace 'installer\FiveMCleaner.iss') -Raw
    if ($issText -notmatch 'IsAutomaticUpdateRelaunch' -or
        $issText -notmatch 'AUTOUPDATE\|no' -or
        $issText -notmatch 'Parameters: "--updated=') {
        throw 'Installer script is missing the gated AUTOUPDATE relaunch contract.'
    }
    if (Test-Path -LiteralPath $autoUpdateLog) {
        Remove-Item -LiteralPath $autoUpdateLog -Force -ErrorAction SilentlyContinue
    }

    Write-Host '6/6 Silent uninstall (preserve user data)...' -ForegroundColor Cyan

    # Simulate the installed app enabling this preference after setup. The
    # uninstaller must still own and remove the product-specific value.
    Set-ItemProperty -LiteralPath $runRegistryPath -Name $runValueName -Value $expectedStartupValue -Type String

    New-Item -ItemType Directory -Force -Path $userDataMarkerRoot | Out-Null
    Set-Content -LiteralPath $userDataMarker -Value "smoke-$smokeId" -Encoding utf8

    $uninstallArguments = @(
        $commonSilentArguments
        '/CLOSEAPPLICATIONS',
        "/LOG=$uninstallLog"
    )
    $uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList $uninstallArguments -WindowStyle Hidden -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Silent uninstall failed with exit code $($uninstallProcess.ExitCode). See $uninstallLog"
    }
    $installed = $false
    Remove-SmokeDesktopShortcut -InstallDirectory $installDirectory

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
    Remove-SmokeDesktopShortcut -InstallDirectory $installDirectory

    if ($installed) {
        $uninstaller = Join-Path $installDirectory 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            $cleanup = Start-Process -FilePath $uninstaller `
                -ArgumentList (@($commonSilentArguments) + @('/CLOSEAPPLICATIONS')) `
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
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
