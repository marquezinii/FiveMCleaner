[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceIconPath,

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$source = [System.IO.Path]::GetFullPath($SourceIconPath)
$output = [System.IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Official icon was not found: $source"
}

Add-Type -AssemblyName System.Drawing

# Inno Setup reserves a 164:314 area for WizardImageFile. Compose a dedicated
# portrait canvas from the official square icon instead of squashing it.
$width = 328
$height = 628
$padding = 24
$iconSize = $width - (2 * $padding)
$outputDirectory = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$sourceImage = $null
$canvas = $null
$graphics = $null
try {
    $sourceImage = [System.Drawing.Image]::FromFile($source)
    $canvas = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($canvas)
    $graphics.Clear([System.Drawing.Color]::FromArgb(21, 21, 21))
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $x = [int](($width - $iconSize) / 2)
    $y = [int](($height - $iconSize) / 2)
    $graphics.DrawImage($sourceImage, $x, $y, $iconSize, $iconSize)
    $canvas.Save($output, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    if ($null -ne $graphics) { $graphics.Dispose() }
    if ($null -ne $canvas) { $canvas.Dispose() }
    if ($null -ne $sourceImage) { $sourceImage.Dispose() }
}

Write-Host "Installer artwork ready: $output ($width x $height)" -ForegroundColor Green
