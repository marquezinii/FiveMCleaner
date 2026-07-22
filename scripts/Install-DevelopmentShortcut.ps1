[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$Build
)

$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$projectPath = Join-Path $workspace 'src\FiveMCleaner.App\FiveMCleaner.App.csproj'
$solutionPath = Join-Path $workspace 'FiveMCleaner.slnx'
$iconPath = Join-Path $workspace 'src\FiveMCleaner.App\Assets\FiveMCleaner.ico'

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "FiveMCleaner.App.csproj was not found under the expected workspace: $workspace"
}

[xml]$project = Get-Content -LiteralPath $projectPath -Raw -Encoding utf8
$propertyGroups = @($project.Project.PropertyGroup)
$targetFramework = $propertyGroups.TargetFramework |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
$assemblyName = $propertyGroups.AssemblyName |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
$outputType = $propertyGroups.OutputType |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($targetFramework) -or
    [string]::IsNullOrWhiteSpace($assemblyName)) {
    throw 'The app project does not declare TargetFramework and AssemblyName.'
}

if ($outputType -ne 'WinExe') {
    throw "The development shortcut requires a Windows GUI executable, but OutputType is '$outputType'."
}

if ($Build -and $PSCmdlet.ShouldProcess($solutionPath, 'Build the Release configuration')) {
    Push-Location $workspace
    try {
        & dotnet build $solutionPath --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Release build failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

$outputDirectory = Join-Path $workspace "src\FiveMCleaner.App\bin\Release\$targetFramework"
$targetPath = [System.IO.Path]::GetFullPath((Join-Path $outputDirectory "$assemblyName.exe"))
$brokerPath = [System.IO.Path]::GetFullPath((Join-Path $outputDirectory 'broker\FiveMCleaner.Broker.exe'))

foreach ($requiredFile in @($targetPath, $brokerPath, $iconPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required development build file was not found: $requiredFile`nRun this script again with -Build."
    }
}

$desktopDirectory = [Environment]::GetFolderPath(
    [Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or
    -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
    throw 'Windows did not return a valid Desktop directory.'
}

$shortcutPath = Join-Path $desktopDirectory 'FiveMCleaner - Simulacao.lnk'
if (-not $PSCmdlet.ShouldProcess($shortcutPath, 'Create or update the --demo development shortcut')) {
    Write-Host "Target: $targetPath --demo"
    return
}

$shell = $null
$shortcut = $null
try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetPath
    $shortcut.Arguments = '--demo'
    $shortcut.WorkingDirectory = $outputDirectory
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.Description = 'FiveMCleaner - simulacao local do build Release atual'
    $shortcut.WindowStyle = 1
    $shortcut.Save()
}
finally {
    if ($null -ne $shortcut) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    }

    if ($null -ne $shell) {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
}

Write-Host "Development shortcut ready: $shortcutPath" -ForegroundColor Green
Write-Host "Target: $targetPath --demo"
