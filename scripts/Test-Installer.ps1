[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [string]$PublishDirectory,

    [string]$ExpectedVersion
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
$upgradeLog = Join-Path $smokeRoot 'upgrade.log'
$uninstallLog = Join-Path $smokeRoot 'uninstall.log'
$uninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{49338651-127F-4FD3-BEAD-88D8C9377672}_is1'
$runRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValueName = 'FiveMCleaner'
$installed = $false

function Assert-UnderArtifacts {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside artifacts: $resolved"
    }
}

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

Assert-UnderArtifacts $smokeRoot

if (Test-Path -LiteralPath $uninstallRegistryPath) {
    throw 'A real FiveMCleaner installation already exists; refusing to replace it during a smoke test.'
}

$existingRunValue = Get-RegistryValueOrNull -Path $runRegistryPath -Name $runValueName
if ($null -ne $existingRunValue) {
    throw 'A FiveMCleaner startup entry already exists; refusing to overwrite it during a smoke test.'
}

& (Join-Path $PSScriptRoot 'Verify-Installer.ps1') `
    -InstallerPath $resolvedInstaller `
    -PublishDirectory $resolvedPublish `
    -ExpectedVersion $ExpectedVersion

New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null

try {
    $installArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/CLOSEAPPLICATIONS',
        '/NORESTARTAPPLICATIONS',
        '/NOICONS',
        '/LANG=ptbr',
        '/TASKS=startup',
        "/DIR=$installDirectory",
        "/GROUP=FiveMCleaner Smoke $smokeId",
        "/LOG=$installLog"
    )
    $installProcess = Start-Process -FilePath $resolvedInstaller -ArgumentList $installArguments -WindowStyle Hidden -Wait -PassThru
    if ($installProcess.ExitCode -ne 0) {
        throw "Silent install failed with exit code $($installProcess.ExitCode). See $installLog"
    }
    $installed = $true

    $installedExecutable = Join-Path $installDirectory 'FiveMCleaner.exe'
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
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
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
    $sourceExecutableHash = (Get-FileHash -LiteralPath (Join-Path $resolvedPublish 'FiveMCleaner.exe') -Algorithm SHA256).Hash
    if ($upgradedExecutableHash -ne $sourceExecutableHash) {
        throw 'Main executable hash mismatch after in-place upgrade.'
    }

    # Simulate the installed app enabling this preference after setup. The
    # uninstaller must still own and remove the product-specific value.
    Set-ItemProperty -LiteralPath $runRegistryPath -Name $runValueName -Value $expectedStartupValue -Type String

    $uninstallArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
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

    # Silent uninstalls intentionally preserve user data. The interactive
    # removal choice is guarded by Verify-Installer.ps1 and is left to manual
    # validation so this smoke test never deletes a developer's real settings.
    if ((Get-Content -LiteralPath (Join-Path $workspace 'installer\FiveMCleaner.iss') -Raw) -notmatch
        "DelTree\(ExpandConstant\('\{localappdata\}\\FiveMCleaner'\), True, True, True\)") {
        throw 'The explicit interactive removal path for user data is missing.'
    }

    Write-Host 'Installer install/upgrade/uninstall smoke test: OK' -ForegroundColor Green
}
finally {
    if ($installed) {
        $uninstaller = Join-Path $installDirectory 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller -PathType Leaf) {
            $cleanup = Start-Process -FilePath $uninstaller `
                -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') `
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

    if (Test-Path -LiteralPath $smokeRoot) {
        Assert-UnderArtifacts $smokeRoot
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}
