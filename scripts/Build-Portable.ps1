[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $workspace 'artifacts'))
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot ".staging-$([Guid]::NewGuid().ToString('N'))"))
$finalRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "FiveMCleaner-$Runtime"))
$archivePath = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "FiveMCleaner-$Runtime.zip"))
$archiveHashPath = "$archivePath.sha256"

function Assert-UnderArtifacts {
    param([string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $prefix = $artifactsRoot.TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside artifacts: $resolved"
    }
}

Assert-UnderArtifacts $stagingRoot
Assert-UnderArtifacts $finalRoot
Assert-UnderArtifacts $archivePath
Assert-UnderArtifacts $archiveHashPath
New-Item -ItemType Directory -Force -Path $artifactsRoot, $stagingRoot | Out-Null

Push-Location $workspace
try {
    & (Join-Path $PSScriptRoot 'Verify-Safety.ps1')

    $brokerOutput = Join-Path $stagingRoot 'broker'
    $appOutput = Join-Path $stagingRoot 'app'
    $pathMap = "$workspace=/_/FiveMCleaner"

    $brokerPublishArguments = @(
        'publish',
        '.\src\FiveMCleaner.Broker\FiveMCleaner.Broker.csproj',
        '--configuration', $Configuration,
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=false',
        '-p:ContinuousIntegrationBuild=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:PathMap=$pathMap",
        '--output', $brokerOutput
    )
    & dotnet @brokerPublishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Broker publish failed.' }

    $appPublishArguments = @(
        'publish',
        '.\src\FiveMCleaner.App\FiveMCleaner.App.csproj',
        '--configuration', $Configuration,
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '-p:BuildProjectReferences=false',
        '-p:PublishSingleFile=false',
        '-p:PublishTrimmed=false',
        '-p:PublishReadyToRun=false',
        '-p:ContinuousIntegrationBuild=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:PathMap=$pathMap",
        '--output', $appOutput
    )
    & dotnet @appPublishArguments
    if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }

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

    if (Test-Path -LiteralPath $finalRoot) {
        Assert-UnderArtifacts $finalRoot
        Remove-Item -LiteralPath $finalRoot -Recurse -Force
    }
    Move-Item -LiteralPath $appOutput -Destination $finalRoot
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
