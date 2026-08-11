[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot ".staging-$([Guid]::NewGuid().ToString('N'))"))
$finalRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "FiveMCleaner-$Runtime"))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "FiveMCleaner-$Runtime.zip"))
$archiveHashPath = "$archivePath.sha256"
$runtimeArchivePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "FiveMCleaner-Runtime-$Runtime.zip"))
$runtimeArchiveHashPath = "$runtimeArchivePath.sha256"

. (Join-Path $PSScriptRoot 'Installer.Common.ps1')

Assert-UnderArtifacts $stagingRoot
Assert-UnderArtifacts $finalRoot
Assert-UnderArtifacts $archivePath
Assert-UnderArtifacts $archiveHashPath
Assert-UnderArtifacts $runtimeArchivePath
Assert-UnderArtifacts $runtimeArchiveHashPath
New-Item -ItemType Directory -Force -Path $artifactsRoot, $stagingRoot | Out-Null

Push-Location $workspace
try {
    & (Join-Path $PSScriptRoot 'Verify-Safety.ps1') -SkipTests

    $brokerOutput = Join-Path $stagingRoot 'broker'
    $launcherOutput = Join-Path $stagingRoot 'launcher'
    $appOutput = Join-Path $stagingRoot 'app'
    $pathMap = "$workspace=/_/FiveMCleaner"
    $version = Get-ProjectVersion -Workspace $workspace
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid stable version: $version" }

    $publishTargets = @(
        @{ Name = 'Broker'; Project = '.\src\FiveMCleaner.Broker\FiveMCleaner.Broker.csproj'; SingleFile = 'false'; SkipProjectReferences = $false; Output = $brokerOutput }
        @{ Name = 'Launcher'; Project = '.\src\FiveMCleaner.Launcher\FiveMCleaner.Launcher.csproj'; SingleFile = 'true'; SkipProjectReferences = $false; Output = $launcherOutput }
        @{ Name = 'App'; Project = '.\src\FiveMCleaner.App\FiveMCleaner.App.csproj'; SingleFile = 'false'; SkipProjectReferences = $true; Output = $appOutput }
    )
    foreach ($target in $publishTargets) {
        $publishArguments = @(
            'publish',
            $target.Project,
            '--configuration', $Configuration,
            '--runtime', $Runtime,
            '--self-contained', 'true'
        )
        if ($target.SkipProjectReferences) {
            $publishArguments += '-p:BuildProjectReferences=false'
        }
        $publishArguments += @(
            "-p:PublishSingleFile=$($target.SingleFile)",
            '-p:PublishTrimmed=false',
            '-p:PublishReadyToRun=false',
            '-p:ContinuousIntegrationBuild=true',
            '-p:DebugType=None',
            '-p:DebugSymbols=false',
            "-p:PathMap=$pathMap",
            '--output', $target.Output
        )
        & dotnet @publishArguments
        if ($LASTEXITCODE -ne 0) { throw "$($target.Name) publish failed." }
    }

    $copiedBroker = Join-Path $appOutput 'broker'
    Assert-UnderArtifacts $copiedBroker
    if (Test-Path -LiteralPath $copiedBroker) {
        Remove-Item -LiteralPath $copiedBroker -Recurse -Force
    }
    foreach ($orphanName in @(
        'FiveMCleaner.Broker.exe',
        'FiveMCleaner.Broker.deps.json',
        'FiveMCleaner.Broker.runtimeconfig.json'
    )) {
        $orphanPath = Join-Path $appOutput $orphanName
        if (Test-Path -LiteralPath $orphanPath) {
            Assert-UnderArtifacts $orphanPath
            Remove-Item -LiteralPath $orphanPath -Force
        }
    }
    Copy-Item -LiteralPath $brokerOutput -Destination $copiedBroker -Recurse

    $brokerOutputPrefix = $copiedBroker.TrimEnd('\') + '\'
    $brokerChecksums = Get-ChildItem -LiteralPath $copiedBroker -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            if (-not $_.FullName.StartsWith($brokerOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to hash a file outside the broker staging directory: $($_.FullName)"
            }
            $relative = $_.FullName.Substring($brokerOutputPrefix.Length).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    Set-Content -LiteralPath (Join-Path $copiedBroker 'SHA256SUMS.txt') -Value $brokerChecksums -Encoding utf8

    Copy-Item -LiteralPath '.\README.md', '.\LICENSE', '.\SECURITY.md', '.\CONTRIBUTING.md', '.\CODE_OF_CONDUCT.md' -Destination $appOutput
    Copy-Item -LiteralPath '.\docs' -Destination (Join-Path $appOutput 'docs') -Recurse

    $appOutputPrefix = $appOutput.TrimEnd('\') + '\'
    $checksums = Get-ChildItem -LiteralPath $appOutput -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            if (-not $_.FullName.StartsWith($appOutputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Refusing to hash a file outside the staging app directory: $($_.FullName)"
            }
            $relative = $_.FullName.Substring($appOutputPrefix.Length).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    Set-Content -LiteralPath (Join-Path $appOutput 'SHA256SUMS.txt') -Value $checksums -Encoding utf8

    foreach ($path in @($runtimeArchivePath, $runtimeArchiveHashPath)) {
        if (Test-Path -LiteralPath $path) {
            Assert-UnderArtifacts $path
            Remove-Item -LiteralPath $path -Force
        }
    }
    Compress-Archive -Path (Join-Path $appOutput '*') -DestinationPath $runtimeArchivePath -CompressionLevel Optimal
    $runtimeArchiveHash = (Get-FileHash -LiteralPath $runtimeArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $runtimeArchiveHashPath -Value "$runtimeArchiveHash  $([System.IO.Path]::GetFileName($runtimeArchivePath))" -Encoding ascii

    if (Test-Path -LiteralPath $finalRoot) {
        Assert-UnderArtifacts $finalRoot
        Remove-Item -LiteralPath $finalRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $finalRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $launcherOutput 'FiveMCleaner.Launcher.exe') -Destination $finalRoot
    $versionRoot = Join-Path $finalRoot "Runtime\versions\$version"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $versionRoot) | Out-Null
    Move-Item -LiteralPath $appOutput -Destination $versionRoot
    [ordered]@{ Version = $version } | ConvertTo-Json -Compress |
        Set-Content -LiteralPath (Join-Path $finalRoot 'Runtime\active.json') -Encoding utf8
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force

    foreach ($path in @($archivePath, $archiveHashPath)) {
        if (Test-Path -LiteralPath $path) {
            Assert-UnderArtifacts $path
            Remove-Item -LiteralPath $path -Force
        }
    }
    Compress-Archive -Path (Join-Path $finalRoot '*') -DestinationPath $archivePath -CompressionLevel Optimal
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $archiveHashPath -Value "$archiveHash  $([System.IO.Path]::GetFileName($archivePath))" -Encoding ascii

    Write-Host "Portable build ready: $finalRoot" -ForegroundColor Green
    Write-Host "Portable archive ready: $archivePath" -ForegroundColor Green
    Write-Host "Atomic runtime archive ready: $runtimeArchivePath" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $stagingRoot) {
        Assert-UnderArtifacts $stagingRoot
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
    throw
}
finally {
    Pop-Location
}
