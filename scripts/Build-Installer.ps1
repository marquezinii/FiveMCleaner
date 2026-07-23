[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$Version,

    [string]$InnoCompilerPath,

    [switch]$SkipPortableBuild,

    [switch]$NoCompilerBootstrap
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$publishDirectory = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'FiveMCleaner-win-x64'))
$installerOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer'))
$installerArtwork = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot 'installer-artwork\FiveMCleaner-wizard-side.png'))
$stagingOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot ".installer-staging-$([Guid]::NewGuid().ToString('N'))"))
$innoVersion = '6.7.3'
$innoAssetName = "innosetup-$innoVersion.exe"
$innoDownloadUrl = "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/$innoAssetName"
$innoSha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$innoCompilerSha256 = '0a8757031b33777e4c9cbffee40f11a5062b36d25cbe144c1db73b6102b80ad7'

function Assert-UnderArtifacts {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside artifacts: $resolved"
    }
}

function Get-ProjectVersion {
    [xml]$props = Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props') -Raw
    $candidate = @($props.Project.PropertyGroup.Version) |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if (-not $candidate) {
        throw 'Directory.Build.props does not define Version.'
    }

    return [string]$candidate
}

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
            throw 'The explicit Inno Setup compiler does not match the pinned 6.7.3 compiler or its Pyrsys B.V. signature is invalid.'
        }
        return $explicit
    }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH)) {
        $candidates += $env:ISCC_PATH
    }
    $candidates += @(
        (Join-Path $artifactsRoot ".tools\inno-$innoVersion\ISCC.exe"),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
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
        throw 'Inno Setup 6 compiler was not found and bootstrap is disabled.'
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
    $Version = Get-ProjectVersion
}

$versionMatch = [regex]::Match($Version, '^(?<core>\d+\.\d+\.\d+)(?<suffix>-[0-9A-Za-z][0-9A-Za-z.-]*)?$')
if (-not $versionMatch.Success) {
    throw "Version must be SemVer-like (for example 1.2.3 or 1.2.3-preview): $Version"
}
$numericVersion = "$($versionMatch.Groups['core'].Value).0"

Assert-UnderArtifacts $publishDirectory
Assert-UnderArtifacts $installerOutput
Assert-UnderArtifacts $installerArtwork
Assert-UnderArtifacts $stagingOutput
New-Item -ItemType Directory -Force -Path $artifactsRoot, $installerOutput, $stagingOutput | Out-Null

try {
    & (Join-Path $PSScriptRoot 'Verify-Installer.ps1') -ScriptOnly

    if (-not $SkipPortableBuild) {
        & (Join-Path $PSScriptRoot 'Build-Portable.ps1') -Runtime win-x64 -Configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw 'Portable self-contained publish failed.'
        }
    }

    foreach ($requiredPayload in @(
        'FiveMCleaner.exe',
        'FiveMCleaner.runtimeconfig.json',
        'coreclr.dll',
        'hostfxr.dll',
        'broker\FiveMCleaner.Broker.exe'
    )) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $requiredPayload) -PathType Leaf)) {
            throw "Publish payload is incomplete: $requiredPayload"
        }
    }

    $compiler = Resolve-InnoCompiler
    Write-Host "Compiling with $compiler" -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot 'New-InstallerArtwork.ps1') `
        -SourceIconPath (Join-Path $workspace 'src\FiveMCleaner.App\Assets\FiveMCleaner.png') `
        -OutputPath $installerArtwork
    if (-not (Test-Path -LiteralPath $installerArtwork -PathType Leaf)) {
        throw 'Installer artwork generation did not produce the expected file.'
    }

    $installerScript = Join-Path $workspace 'installer\FiveMCleaner.iss'
    $arguments = @(
        '/Qp',
        "/DAppVersion=$Version",
        "/DAppNumericVersion=$numericVersion",
        "/DSourceDir=$publishDirectory",
        "/DOutputDir=$stagingOutput",
        "/DRepositoryRoot=$workspace",
        "/DInstallerArtworkPath=$installerArtwork",
        $installerScript
    )
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    $baseName = "FiveMCleaner-Setup-$Version-win-x64"
    $stagedInstaller = Join-Path $stagingOutput "$baseName.exe"
    $stagedContents = Join-Path $stagingOutput "$baseName.contents.txt"
    if (-not (Test-Path -LiteralPath $stagedInstaller -PathType Leaf)) {
        throw "Compiled installer was not created: $stagedInstaller"
    }

    $finalInstaller = Join-Path $installerOutput "$baseName.exe"
    $finalContents = Join-Path $installerOutput "$baseName.contents.txt"
    $finalHash = "$finalInstaller.sha256"
    $releaseManifest = Join-Path $installerOutput "FiveMCleaner-release-manifest-$Version.json"
    $stagedHash = "$stagedInstaller.sha256"
    $stagedReleaseManifest = Join-Path $stagingOutput "FiveMCleaner-release-manifest-$Version.json"
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
    $gitStatus = @(& git -C $workspace status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the source worktree for the release manifest.'
    }

    $portableArchive = Join-Path $artifactsRoot 'FiveMCleaner-win-x64.zip'
    if (-not (Test-Path -LiteralPath $portableArchive -PathType Leaf)) {
        throw "Portable archive not found: $portableArchive"
    }
    $portableHash = (Get-FileHash -LiteralPath $portableArchive -Algorithm SHA256).Hash.ToLowerInvariant()

    $manifest = [ordered]@{
        schemaVersion = 1
        product = 'FiveMCleaner'
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
            mainExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $publishDirectory 'FiveMCleaner.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
            brokerExecutableSha256 = (Get-FileHash -LiteralPath (Join-Path $publishDirectory 'broker\FiveMCleaner.Broker.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
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
            }
        )
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stagedReleaseManifest -Encoding utf8

    & (Join-Path $PSScriptRoot 'Verify-Installer.ps1') `
        -InstallerPath $stagedInstaller `
        -PublishDirectory $publishDirectory `
        -ExpectedVersion $Version

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
