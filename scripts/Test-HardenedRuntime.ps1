[CmdletBinding()]
param(
    # The assembled portable runtime tree (artifacts/FiveMCleaner-win-x64),
    # containing Runtime\versions\<version>\FiveMCleaner.exe.
    [Parameter(Mandatory)]
    [string]$RuntimeDirectory,

    [Parameter(Mandatory)]
    [string]$Version,

    # Seconds to allow the capture smoke to render and exit before it is
    # treated as a hang.
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runtimeRoot = [System.IO.Path]::GetFullPath($RuntimeDirectory)
$appExecutable = Join-Path $runtimeRoot "Runtime\versions\$Version\FiveMCleaner.exe"
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Hardened app executable not found: $appExecutable"
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("fmc-harden-smoke-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null

# Rendering each page exercises a different slice of the obfuscated runtime:
# Optimizer drives Core planning + the 3D scene; Overview drives the Windows
# diagnostics adapters. If obfuscation broke a renamed member or a string that
# is looked up at runtime, the app throws before the PNG is written.
$pages = @('Optimizer', 'Overview')

try {
    foreach ($page in $pages) {
        $outputPng = Join-Path $smokeRoot "capture-$page.png"
        $arguments = @(
            '--demo-synthetic',
            "--capture=`"$outputPng`"",
            "--capture-page=$page"
        )

        $process = Start-Process -FilePath $appExecutable -ArgumentList $arguments -PassThru
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill($true) } catch { }
            throw "Hardened runtime smoke for page '$page' did not exit within $TimeoutSeconds s."
        }
        if ($process.ExitCode -ne 0) {
            throw "Hardened runtime smoke for page '$page' exited with code $($process.ExitCode)."
        }
        if (-not (Test-Path -LiteralPath $outputPng -PathType Leaf)) {
            throw "Hardened runtime smoke for page '$page' produced no capture; the obfuscated app failed to render it."
        }
        if ((Get-Item -LiteralPath $outputPng).Length -le 0) {
            throw "Hardened runtime smoke for page '$page' produced an empty capture."
        }
        Write-Host "Hardened runtime rendered '$page' successfully." -ForegroundColor Green
    }

    Write-Host 'Post-obfuscation runtime smoke passed.' -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $smokeRoot) {
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }
}
