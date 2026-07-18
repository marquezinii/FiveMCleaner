[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot

function Find-CSharpSourceMatches {
    param(
        [Parameter(Mandatory)]
        [string[]]$Roots,

        [Parameter(Mandatory)]
        [string]$Pattern
    )

    foreach ($root in $Roots) {
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.cs') {
            if ($file.FullName -match '[\\/](bin|obj)[\\/]') {
                continue
            }

            foreach ($matchInfo in Select-String -LiteralPath $file.FullName -Pattern $Pattern) {
                "$($matchInfo.Path):$($matchInfo.LineNumber):$($matchInfo.Line.Trim())"
            }
        }
    }
}

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
    $forbiddenMatches = @(Find-CSharpSourceMatches -Roots $scanRoots -Pattern $forbiddenPattern)
    if ($forbiddenMatches.Count -gt 0) {
        throw "Forbidden tweak or security-bypass pattern found:`n$($forbiddenMatches -join [Environment]::NewLine)"
    }

    $contractRoots = @(
        'src/FiveMCleaner.Contracts',
        'src/FiveMCleaner.Core'
    )
    $commandFields = @(
        Find-CSharpSourceMatches `
            -Roots $contractRoots `
            -Pattern '(public|required|init).*\b(command|script|arguments?|registryPath|executablePath)\b'
    )
    if ($commandFields.Count -gt 0) {
        throw "A command-like field leaked into plan contracts:`n$($commandFields -join [Environment]::NewLine)"
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
