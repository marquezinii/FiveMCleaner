[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
Push-Location $workspace
try {
    $forbiddenPattern = @(
        'bcdedit(\.exe)?\s',
        'useplatformclock',
        'useplatformtick',
        'disabledynamictick',
        'Set-MpPreference',
        'Add-MpPreference',
        'DisableAntiSpyware',
        'ExclusionPath',
        'netsh\s+int\s+tcp',
        'EmptyStandbyList',
        'ProcessPriorityClass\.RealTime',
        'commandline\.txt'
    ) -join '|'

    $scanRoots = @(
        'src/FiveMCleaner.Windows',
        'src/FiveMCleaner.Broker'
    )
    $matches = & rg -n -i --glob '*.cs' --glob '!bin/**' --glob '!obj/**' -- $forbiddenPattern @scanRoots 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw "Forbidden tweak or security-bypass pattern found:`n$($matches -join [Environment]::NewLine)"
    }
    if ($LASTEXITCODE -ne 1) {
        throw "Safety scan could not run (rg exit code $LASTEXITCODE)."
    }

    $contractScanArguments = @(
        '-n',
        '-i',
        '--glob', '*.cs',
        '--glob', '!bin/**',
        '--glob', '!obj/**',
        '--',
        '(public|required|init).*\b(command|script|arguments?|registryPath|executablePath)\b',
        'src/FiveMCleaner.Contracts',
        'src/FiveMCleaner.Core'
    )
    $commandFields = & rg @contractScanArguments 2>$null
    if ($LASTEXITCODE -eq 0) {
        throw "A command-like field leaked into plan contracts:`n$($commandFields -join [Environment]::NewLine)"
    }
    if ($LASTEXITCODE -ne 1) {
        throw "Contract scan could not run (rg exit code $LASTEXITCODE)."
    }

    dotnet build '.\FiveMCleaner.slnx' --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    if (-not $SkipTests) {
        dotnet test '.\tests\FiveMCleaner.Tests\FiveMCleaner.Tests.csproj' --configuration Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    Write-Host 'Safety verification passed.' -ForegroundColor Green
}
finally {
    Pop-Location
}
