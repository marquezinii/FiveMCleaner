[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$Version,

    [string]$InnoCompilerPath,

    [switch]$SkipPortableBuild,

    [switch]$NoCompilerBootstrap,

    [switch]$AllowDirtySource,

    # Forwarded to Build-Portable: obfuscate the internal-logic assemblies in
    # the published runtime before it is packaged, hashed and signed. Used by
    # the public release workflow. With -SkipPortableBuild it verifies that
    # the existing staged payload is still hardened before repackaging it.
    [switch]$Harden
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'Ralven-win-x64'))
$installerOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer'))
$installerArtworkLight = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer-artwork\Ralven-wizard-side-light.png'))
$installerArtworkDark = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer-artwork\Ralven-wizard-side-dark.png'))
$installerDocuments = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer-documents'))
$installerLicense = Join-Path $installerDocuments 'license.rtf'
$installerInfoEnglish = Join-Path $installerDocuments 'install-info.en.rtf'
$installerInfoPortuguese = Join-Path $installerDocuments 'install-info.pt-BR.rtf'
$installerInfoSpanish = Join-Path $installerDocuments 'install-info.es.rtf'
$installerInfoFrench = Join-Path $installerDocuments 'install-info.fr.rtf'
$stagingOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot ".installer-staging-$([Guid]::NewGuid().ToString('N'))"))
$innoVersion = '7.0.2'
$innoAssetName = "innosetup-$innoVersion-x64.exe"
$innoDownloadUrl = "https://github.com/jrsoftware/issrc/releases/download/is-7_0_2/$innoAssetName"
$innoSha256 = '5ad54ca3def786f8f4212552e54cc6d8d61329e2d24a1cfee0571d42c2684ff1'
$innoCompilerSha256 = '0ff6140d641f84b64204a2c4d52207c6fc437c9f4db8779c83083d84f7e3d70d'

. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

function Test-InnoCompilerTrust {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $false
    }
    $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $innoCompilerSha256) {
        return $false
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    return $signature.Status -eq 'Valid' -and
        $null -ne $signature.SignerCertificate -and
        $signature.SignerCertificate.Subject -match 'CN=Pyrsys B\.V\.'
}

function Resolve-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
        $explicit = [System.IO.Path]::GetFullPath($InnoCompilerPath)
        if (-not (Test-Path -LiteralPath $explicit -PathType Leaf)) {
            throw "Inno Setup compiler not found: $explicit"
        }
        if (-not (Test-InnoCompilerTrust -Path $explicit)) {
            throw "The explicit Inno Setup compiler does not match the pinned $innoVersion x64 compiler or its Pyrsys B.V. signature is invalid."
        }
        return $explicit
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH)) {
        $candidates += $env:ISCC_PATH
    }
    $candidates += @(
        (Join-Path $artifactsRoot ".tools\inno-$innoVersion\ISCC.exe"),
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
    )
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $candidates += $command.Source
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-InnoCompilerTrust -Path $candidate)) {
            return [System.IO.Path]::GetFullPath($candidate)
        }
    }

    if ($NoCompilerBootstrap) {
        throw "Inno Setup $innoVersion x64 compiler was not found and bootstrap is disabled."
    }

    $downloads = Join-Path $artifactsRoot '.tools\downloads'
    $compilerRoot = Join-Path $artifactsRoot ".tools\inno-$innoVersion"
    $downloadPath = Join-Path $downloads $innoAssetName
    foreach ($path in @($downloads, $compilerRoot, $downloadPath)) {
        Assert-UnderArtifacts $path
    }
    New-Item -ItemType Directory -Force -Path $downloads, $compilerRoot | Out-Null

    $mustDownload = $true
    if (Test-Path -LiteralPath $downloadPath -PathType Leaf) {
        $cachedHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $mustDownload = $cachedHash -ne $innoSha256
        if ($mustDownload) {
            Remove-Item -LiteralPath $downloadPath -Force
        }
    }

    if ($mustDownload) {
        Write-Host "Downloading pinned Inno Setup $innoVersion compiler..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $innoDownloadUrl -OutFile $downloadPath -MaximumRedirection 5
    }

    $actualHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $innoSha256) {
        throw "Pinned Inno Setup SHA-256 mismatch: $actualHash"
    }

    $downloadSignature = Get-AuthenticodeSignature -LiteralPath $downloadPath
    $publisher = $downloadSignature.SignerCertificate.Subject
    if ($downloadSignature.Status -ne 'Valid' -or $publisher -notmatch 'CN=Pyrsys B\.V\.') {
        throw "Inno Setup bootstrap signature is not valid for Pyrsys B.V. Status: $($downloadSignature.Status)."
    }

    Write-Host 'Installing the verified compiler in the local artifacts tool cache...' -ForegroundColor Cyan
    $bootstrapArguments = @(
        '/PORTABLE=1',
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/DIR=`"$compilerRoot`""
    )
    $bootstrapProcess = Start-Process `
        -FilePath $downloadPath `
        -ArgumentList $bootstrapArguments `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($bootstrapProcess.ExitCode -ne 0) {
        throw "Inno Setup compiler bootstrap failed with exit code $($bootstrapProcess.ExitCode)."
    }

    $bootstrapped = Join-Path $compilerRoot 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $bootstrapped -PathType Leaf)) {
        throw "Bootstrapped compiler was not found: $bootstrapped"
    }
    if (-not (Test-InnoCompilerTrust -Path $bootstrapped)) {
        throw 'Bootstrapped compiler failed the pinned hash or Authenticode trust check.'
    }

    return $bootstrapped
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -Workspace $workspace
}

$versionMatch = [regex]::Match($Version, '^\d+\.\d+\.\d+$')
if (-not $versionMatch.Success) {
    throw "Version must be numeric SemVer (for example 1.2.3): $Version"
}
$numericVersion = "$Version.0"

Assert-UnderArtifacts $publishDirectory
Assert-UnderArtifacts $installerOutput
Assert-UnderArtifacts $installerArtworkLight
Assert-UnderArtifacts $installerArtworkDark
Assert-UnderArtifacts $installerDocuments
Assert-UnderArtifacts $installerLicense
Assert-UnderArtifacts $installerInfoEnglish
Assert-UnderArtifacts $installerInfoPortuguese
Assert-UnderArtifacts $installerInfoSpanish
Assert-UnderArtifacts $installerInfoFrench
Assert-UnderArtifacts $stagingOutput
New-Item -ItemType Directory -Force -Path $artifactsRoot, $installerOutput, $stagingOutput | Out-Null

try {
    & (Join-Path $PSScriptRoot 'New-InstallerDocuments.ps1') `
        -LicensePath (Join-Path $workspace 'LICENSE') `
        -EnglishInfoPath (Join-Path $workspace 'installer\install-info.en.txt') `
        -PortugueseInfoPath (Join-Path $workspace 'installer\install-info.pt-BR.txt') `
        -SpanishInfoPath (Join-Path $workspace 'installer\install-info.es.txt') `
        -FrenchInfoPath (Join-Path $workspace 'installer\install-info.fr.txt') `
        -OutputDirectory $installerDocuments

    & (Join-Path $PSScriptRoot 'Verify-Installer.ps1') -ScriptOnly

    $gitStatusProbe = @(& git -C $workspace status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the source worktree before building the installer.'
    }
    $requireCleanSource = -not $AllowDirtySource -and (
        -not [string]::IsNullOrWhiteSpace($env:GITHUB_ACTIONS) -or
        -not [string]::IsNullOrWhiteSpace($env:CI)
    )
    if ($requireCleanSource -and $gitStatusProbe.Count -ne 0) {
        throw 'Refusing to build a release installer from a dirty worktree. Commit or stash local changes first.'
    }

    if (-not $SkipPortableBuild) {
        $portableArguments = @{ Runtime = 'win-x64'; Configuration = $Configuration }
        if ($Harden) { $portableArguments['Harden'] = $true }
        & (Join-Path $PSScriptRoot 'Build-Portable.ps1') @portableArguments
        if ($LASTEXITCODE -ne 0) {
            throw 'Portable self-contained publish failed.'
        }
    }

    foreach ($requiredPayload in @(
        'Ralven.Launcher.exe',
        'Runtime\active.json',
        "Runtime\versions\$Version\Ralven.exe",
        "Runtime\versions\$Version\Ralven.runtimeconfig.json",
        "Runtime\versions\$Version\coreclr.dll",
        "Runtime\versions\$Version\hostfxr.dll",
        "Runtime\versions\$Version\broker\Ralven.Broker.exe"
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredPayload) -PathType Leaf)) {
            throw "Publish payload is incomplete: $requiredPayload"
        }
    }

    $compiler = Resolve-InnoCompiler
    Write-Host "Compiling with $compiler" -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot 'New-InstallerArtwork.ps1') `
        -SourceIconPath (Join-Path $workspace 'src\Ralven.App\Assets\Ralven.png') `
        -OutputPath $installerArtworkLight `
        -OutputPathDark $installerArtworkDark
    foreach ($artwork in @($installerArtworkLight, $installerArtworkDark)) {
        if (-not (Test-Path -LiteralPath $artwork -PathType Leaf)) {
            throw "Installer artwork generation did not produce the expected file: $artwork"
        }
    }

    $installerScript = Join-Path $workspace 'installer\Ralven.iss'
    $arguments = @(
        '/Qp',
        "/DAppVersion=$Version",
        "/DAppNumericVersion=$numericVersion",
        "/DSourceDir=$publishDirectory",
        "/DOutputDir=$stagingOutput",
        "/DRepositoryRoot=$workspace",
        "/DInstallerArtworkPath=$installerArtworkLight",
        "/DInstallerArtworkPathDark=$installerArtworkDark",
        "/DInstallerLicensePath=$installerLicense",
        "/DInstallerInfoEnglishPath=$installerInfoEnglish",
        "/DInstallerInfoPortuguesePath=$installerInfoPortuguese",
        "/DInstallerInfoSpanishPath=$installerInfoSpanish",
        "/DInstallerInfoFrenchPath=$installerInfoFrench",
        $installerScript
    )
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    $baseName = "Ralven-Setup-$Version-win-x64"
    $stagedInstaller = Join-Path $stagingOutput "$baseName.exe"
    $stagedContents = Join-Path $stagingOutput "$baseName.contents.txt"
    if (-not (Test-Path -LiteralPath $stagedInstaller -PathType Leaf)) {
        throw "Compiled installer was not created: $stagedInstaller"
    }

    $finalInstaller = Join-Path $installerOutput "$baseName.exe"
    $finalContents = Join-Path $installerOutput "$baseName.contents.txt"
    $finalHash = "$finalInstaller.sha256"
    $releaseManifest = Join-Path $installerOutput "Ralven-release-manifest-$Version.json"
    $stagedHash = "$stagedInstaller.sha256"
    $stagedReleaseManifest = Join-Path $stagingOutput "Ralven-release-manifest-$Version.json"
    foreach ($path in @(
        $finalInstaller,
        $finalContents,
        $finalHash,
        $releaseManifest,
        $stagedHash,
        $stagedReleaseManifest
    )) {
        Assert-UnderArtifacts $path
    }

    $installerHash = (Get-FileHash -LiteralPath $stagedInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $stagedHash -Value "$installerHash  $([System.IO.Path]::GetFileName($finalInstaller))" -Encoding ascii

    $payloadFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File)
    $gitCommit = (& git -C $workspace rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $gitCommit -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the source commit for the release manifest.'
    }
    $gitStatus = $gitStatusProbe

    $portableArchive = Join-Path $artifactsRoot 'Ralven-win-x64.zip'
    $runtimeArchive = Join-Path $artifactsRoot 'Ralven-Runtime-win-x64.zip'
    if (-not (Test-Path -LiteralPath $portableArchive -PathType Leaf)) {
        throw "Portable archive not found: $portableArchive"
    }
    $portableHash = (Get-FileHash -LiteralPath $portableArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not (Test-Path -LiteralPath $runtimeArchive -PathType Leaf)) {
        throw "Atomic runtime archive not found: $runtimeArchive"
    }
    $runtimeHash = (Get-FileHash -LiteralPath $runtimeArchive -Algorithm SHA256).Hash.ToLowerInvariant()

    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'Ralven'
        version = $Version
        runtime = 'win-x64'
        selfContained = $true
        minimumWindowsBuild = 19041
        sourceCommit = $gitCommit
        sourceDirty = $gitStatus.Count -ne 0
        generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        payload = [ordered]@{
            fileCount = $payloadFiles.Count
            sizeBytes = [long](($payloadFiles | Measure-Object -Property Length -Sum).Sum)
            launcherExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $publishDirectory 'Ralven.Launcher.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
            mainExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $publishDirectory "Runtime\versions\$Version\Ralven.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
            brokerExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $publishDirectory "Runtime\versions\$Version\broker\Ralven.Broker.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        artifacts = @(
            [ordered]@{
                name = [System.IO.Path]::GetFileName($finalInstaller)
                sizeBytes = (Get-Item -LiteralPath $stagedInstaller).Length
                sha256 = $installerHash
            },
            [ordered]@{
                name = [System.IO.Path]::GetFileName($portableArchive)
                sizeBytes = (Get-Item -LiteralPath $portableArchive).Length
                sha256 = $portableHash
            },
            [ordered]@{
                name = [System.IO.Path]::GetFileName($runtimeArchive)
                sizeBytes = (Get-Item -LiteralPath $runtimeArchive).Length
                sha256 = $runtimeHash
            }
        )
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stagedReleaseManifest -Encoding utf8

    & (Join-Path $PSScriptRoot 'Verify-Installer.ps1') `
        -InstallerPath $stagedInstaller `
        -PublishDirectory $publishDirectory `
        -ExpectedVersion $Version

    if ($Harden) {
        # Revalidate the runtime, both ZIPs and the compiled installer whether
        # the portable payload was built now or supplied by the post-signing
        # broker finalizer.
        & (Join-Path $PSScriptRoot 'Test-NoUnobfuscatedAssemblies.ps1') `
            -RuntimeDirectory $publishDirectory `
            -Version $Version `
            -PortableZipPath $portableArchive `
            -RuntimeZipPath $runtimeArchive `
            -InstallerPath $stagedInstaller
        if ($LASTEXITCODE -ne 0) { throw 'Fail-closed hardening verification failed for the installer.' }
    }

    foreach ($path in @($finalInstaller, $finalContents, $finalHash, $releaseManifest)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    Move-Item -LiteralPath $stagedInstaller -Destination $finalInstaller
    Move-Item -LiteralPath $stagedHash -Destination $finalHash
    Move-Item -LiteralPath $stagedReleaseManifest -Destination $releaseManifest
    if (Test-Path -LiteralPath $stagedContents -PathType Leaf) {
        Move-Item -LiteralPath $stagedContents -Destination $finalContents
    }

    Write-Host "Installer ready: $finalInstaller" -ForegroundColor Green
    Write-Host "SHA-256: $installerHash" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $stagingOutput) {
        Assert-UnderArtifacts $stagingOutput
        Remove-Item -LiteralPath $stagingOutput -Recurse -Force
    }
}
