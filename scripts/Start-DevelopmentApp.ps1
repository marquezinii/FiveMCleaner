[CmdletBinding()]
param(
    [switch]$NoLaunch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solutionPath = Join-Path $workspace 'FiveMCleaner.slnx'
$projectPath = Join-Path $workspace 'src\FiveMCleaner.App\FiveMCleaner.App.csproj'
$artifactsDirectory = Join-Path $workspace 'artifacts'
$buildLogPath = Join-Path $artifactsDirectory 'development-launcher-build.log'

foreach ($requiredPath in @($solutionPath, $projectPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required development workspace file was not found: $requiredPath"
    }
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw 'The .NET SDK is required to run the FiveMCleaner development launcher.'
}

New-Item -ItemType Directory -Path $artifactsDirectory -Force | Out-Null
$buildOutput = & $dotnet.Source build $solutionPath --configuration Release --nologo 2>&1
$buildOutput | Set-Content -LiteralPath $buildLogPath -Encoding utf8

if ($LASTEXITCODE -ne 0) {
    Add-Type -AssemblyName PresentationFramework
    [void][System.Windows.MessageBox]::Show(
        "O FiveMCleaner não pôde ser preparado para execução. O registro do build está em:`n$buildLogPath",
        'FiveMCleaner - Desenvolvimento',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error)
    exit $LASTEXITCODE
}

$propertyOutput = @(& $dotnet.Source msbuild $projectPath -nologo `
        -property:Configuration=Release `
        -getProperty:TargetFramework `
        -getProperty:AssemblyName `
        -getProperty:TargetDir 2>&1)
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild could not resolve the app output properties:`n$($propertyOutput -join "`n")"
}

try {
    $projectProperties = (($propertyOutput -join "`n") | ConvertFrom-Json -ErrorAction Stop).Properties
}
catch {
    throw "MSBuild returned invalid app output properties: $($propertyOutput -join "`n")"
}

$targetFramework = [string]$projectProperties.TargetFramework
$assemblyName = [string]$projectProperties.AssemblyName
$outputDirectory = [string]$projectProperties.TargetDir
if ([string]::IsNullOrWhiteSpace($targetFramework) -or
    [string]::IsNullOrWhiteSpace($assemblyName) -or
    [string]::IsNullOrWhiteSpace($outputDirectory)) {
    throw 'MSBuild did not resolve TargetFramework, AssemblyName and TargetDir for the app.'
}

$applicationPath = Join-Path $outputDirectory "$assemblyName.exe"
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "The Release build completed but the application executable was not found: $applicationPath"
}

if ($NoLaunch) {
    Write-Host "Development build is ready: $applicationPath" -ForegroundColor Green
    return
}

# Marks this run as Development for remote crash reporting (Sentry), so it
# never mixes with end-user errors reported by installed Production copies.
# See FiveMCleaner.App/Services/AppEnvironment.cs.
$env:FIVEMCLEANER_ENVIRONMENT = 'Development'

# The development launcher must remain useful on a machine that does not have
# FiveM or GTA V installed. The explicit demo switch supplies a realistic,
# deterministic Legacy diagnostic and simulates the complete optimization
# flow without reading or writing game files, invoking the broker, or showing
# UAC. Production launchers never pass this switch, so they keep the real
# installation detection and optimization preconditions.
Start-Process -FilePath $applicationPath -ArgumentList '--demo-synthetic' -WorkingDirectory $outputDirectory
