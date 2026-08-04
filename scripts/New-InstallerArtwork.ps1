[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceIconPath,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$OutputPathDark
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = [System.IO.Path]::GetFullPath($SourceIconPath)
$outputLight = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Official icon was not found: $source"
}

if ([string]::IsNullOrWhiteSpace($OutputPathDark)) {
    $directory = Split-Path -Parent $outputLight
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($outputLight)
    if ($baseName -match '-(light|dark)$') {
        $baseName = $baseName -replace '-(light|dark)$', ''
    }
    $OutputPathDark = Join-Path $directory "$baseName-dark.png"
    if ($outputLight -notmatch '-light\.png$') {
        $outputLight = Join-Path $directory "$baseName-light.png"
    }
}

$outputDark = [System.IO.Path]::GetFullPath($OutputPathDark)

Add-Type -AssemblyName System.Drawing

# Inno Setup reserves a 164:314 area for WizardImageFile. Compose dedicated
# portrait canvases from the official square icon instead of squashing it.
$width = 328
$height = 628
$padding = 24
$iconSize = $width - (2 * $padding)
foreach ($path in @($outputLight, $outputDark)) {
    $outputDirectory = Split-Path -Parent $path
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

function Save-WizardCanvas {
    param(
        [Parameter(Mandatory)][System.Drawing.Image]$SourceImage,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][System.Drawing.Color]$Background
    )

    $canvas = $null
    $graphics = $null
    try {
        $canvas = New-Object System.Drawing.Bitmap($width, $height)
        $graphics = [System.Drawing.Graphics]::FromImage($canvas)
        $graphics.Clear($Background)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $x = [int](($width - $iconSize) / 2)
        $y = [int](($height - $iconSize) / 2)
        $graphics.DrawImage($SourceImage, $x, $y, $iconSize, $iconSize)
        $canvas.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        if ($null -ne $graphics) { $graphics.Dispose() }
        if ($null -ne $canvas) { $canvas.Dispose() }
    }
}

$sourceImage = $null
try {
    $sourceImage = [System.Drawing.Image]::FromFile($source)
    Save-WizardCanvas -SourceImage $sourceImage -Path $outputLight -Background ([System.Drawing.Color]::FromArgb(243, 244, 246))
    Save-WizardCanvas -SourceImage $sourceImage -Path $outputDark -Background ([System.Drawing.Color]::FromArgb(21, 21, 21))
}
finally {
    if ($null -ne $sourceImage) { $sourceImage.Dispose() }
}

Write-Host "Installer artwork ready: $outputLight / $outputDark ($width x $height)" -ForegroundColor Green
