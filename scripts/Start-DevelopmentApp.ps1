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

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding utf8
$propertyGroups = @($project.Project.PropertyGroup)
$targetFramework = $propertyGroups.TargetFramework |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
$assemblyName = $propertyGroups.AssemblyName |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($targetFramework) -or
    [string]::IsNullOrWhiteSpace($assemblyName)) {
    throw 'The app project does not declare TargetFramework and AssemblyName.'
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

$outputDirectory = Join-Path $workspace "src\FiveMCleaner.App\bin\Release\$targetFramework"
$applicationPath = Join-Path $outputDirectory "$assemblyName.exe"
if (-not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    throw "The Release build completed but the application executable was not found: $applicationPath"
}

if ($NoLaunch) {
    Write-Host "Development build is ready: $applicationPath" -ForegroundColor Green
    return
}

Start-Process -FilePath $applicationPath -WorkingDirectory $outputDirectory
