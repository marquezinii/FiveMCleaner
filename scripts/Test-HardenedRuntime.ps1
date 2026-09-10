[CmdletBinding()]
param(
    # The assembled portable runtime tree (artifacts/Ralven-win-x64),
    # containing Runtime\versions\<version>\Ralven.exe.
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
$appExecutable = Join-Path $runtimeRoot "Runtime\versions\$Version\Ralven.exe"
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Hardened app executable not found: $appExecutable"
}

$smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("fmc-harden-smoke-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null

# Render the main navigation surfaces that cross Core/Windows, WPF bindings,
# authentication configuration, Ralven AI and settings. Demo mode keeps this
# post-obfuscation smoke deterministic and prevents external mutations/network.
$pages = @(
    'Overview',
    'System',
    'Applications',
    'Games',
    'FiveM',
    'Optimizer',
    'FiveMOptimizer',
    'History',
    'Settings',
    'Pro',
    'RalvenAi',
    'Ultra'
)

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
