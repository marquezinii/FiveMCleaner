[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$workspace = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $workspace 'src\FiveMCleaner.App\Assets'
$docsDirectory = Join-Path $workspace 'docs\assets'
$masterPath = Join-Path $assetDirectory 'FiveMCleaner-master.png'
$appPngPath = Join-Path $assetDirectory 'FiveMCleaner.png'
$icoPath = Join-Path $assetDirectory 'FiveMCleaner.ico'

if (-not (Test-Path -LiteralPath $masterPath)) {
    throw "Official transparent master not found: $masterPath"
}

New-Item -ItemType Directory -Force -Path $assetDirectory, $docsDirectory | Out-Null

function Get-AlphaBounds {
    param([System.Drawing.Bitmap]$Bitmap)

    $rect = [System.Drawing.Rectangle]::new(0, 0, $Bitmap.Width, $Bitmap.Height)
    $data = $Bitmap.LockBits(
        $rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )

    try {
        $stride = [Math]::Abs($data.Stride)
        $bytes = [byte[]]::new($stride * $Bitmap.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $minX = $Bitmap.Width
        $minY = $Bitmap.Height
        $maxX = -1
        $maxY = -1

        for ($y = 0; $y -lt $Bitmap.Height; $y++) {
            $row = $y * $stride
            for ($x = 0; $x -lt $Bitmap.Width; $x++) {
                if ($bytes[$row + ($x * 4) + 3] -gt 8) {
                    if ($x -lt $minX) { $minX = $x }
                    if ($x -gt $maxX) { $maxX = $x }
                    if ($y -lt $minY) { $minY = $y }
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }

        if ($maxX -lt 0) {
            throw 'Official icon master contains no visible pixels.'
        }

        return [System.Drawing.Rectangle]::new($minX, $minY, $maxX - $minX + 1, $maxY - $minY + 1)
    }
    finally {
        $Bitmap.UnlockBits($data)
    }
}

function New-SquareIcon {
    param(
        [System.Drawing.Bitmap]$Source,
        [System.Drawing.Rectangle]$ContentBounds,
        [int]$Size,
        [double]$Padding = 0.055
    )

    $output = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $output.SetResolution(96, 96)
    $graphics = [System.Drawing.Graphics]::FromImage($output)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $available = $Size * (1 - (2 * $Padding))
    $scale = [Math]::Min($available / $ContentBounds.Width, $available / $ContentBounds.Height)
    $width = [int][Math]::Round($ContentBounds.Width * $scale)
    $height = [int][Math]::Round($ContentBounds.Height * $scale)
    $x = [int][Math]::Round(($Size - $width) / 2)
    $y = [int][Math]::Round(($Size - $height) / 2)
    $destination = [System.Drawing.Rectangle]::new($x, $y, $width, $height)
    $graphics.DrawImage($Source, $destination, $ContentBounds, [System.Drawing.GraphicsUnit]::Pixel)
    $graphics.Dispose()
    return $output
}

function New-RoundedPath {
    param([System.Drawing.RectangleF]$Bounds, [single]$Radius)

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $diameter = $Radius * 2
    $path.AddArc($Bounds.X, $Bounds.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.X, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$master = [System.Drawing.Bitmap]::new($masterPath)
$bounds = Get-AlphaBounds -Bitmap $master

$appIcon = New-SquareIcon -Source $master -ContentBounds $bounds -Size 1024
$appIcon.Save($appPngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$docsIcon = New-SquareIcon -Source $master -ContentBounds $bounds -Size 512
$docsIcon.Save((Join-Path $docsDirectory 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$payloads = [System.Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $frame = New-SquareIcon -Source $master -ContentBounds $bounds -Size $size -Padding 0.045
    $stream = [System.IO.MemoryStream]::new()
    $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $payloads.Add($stream.ToArray())
    $stream.Dispose()
    $frame.Dispose()
}

$file = [System.IO.File]::Open($icoPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $bytes = $payloads[$index]
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$bytes.Length)
    $writer.Write([uint32]$offset)
    $offset += $bytes.Length
}
foreach ($bytes in $payloads) {
    $writer.Write($bytes)
}
$writer.Dispose()

$hero = [System.Drawing.Bitmap]::new(1280, 460, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$heroGraphics = [System.Drawing.Graphics]::FromImage($hero)
$heroGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$heroGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$heroBounds = [System.Drawing.RectangleF]::new(0, 0, 1280, 460)
$heroBackground = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    $heroBounds,
    [System.Drawing.Color]::FromArgb(255, 24, 26, 30),
    [System.Drawing.Color]::FromArgb(255, 9, 10, 12),
    24
)
$heroGraphics.FillRectangle($heroBackground, $heroBounds)
$heroGraphics.FillEllipse([System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(22, 255, 116, 20)), 785, -90, 560, 560)

$badgeBounds = [System.Drawing.RectangleF]::new(88, 88, 174, 31)
$badgePath = New-RoundedPath -Bounds $badgeBounds -Radius 15
$badgeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(32, 255, 122, 24))
$heroGraphics.FillPath($badgeBrush, $badgePath)
$badgeFont = [System.Drawing.Font]::new('Segoe UI', 10, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Point)
$heroGraphics.DrawString('WINDOWS NATIVE', $badgeFont, [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 169, 96)), 107, 95)

$titleFont = [System.Drawing.Font]::new('Segoe UI', 46, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Point)
$whiteBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 246, 247, 249))
$orangeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 255, 122, 24))
$titleOrigin = [System.Drawing.PointF]::new(84, 135)
$heroGraphics.DrawString('FiveM', $titleFont, $whiteBrush, $titleOrigin)
$fiveMWidth = $heroGraphics.MeasureString('FiveM', $titleFont).Width - 18
$heroGraphics.DrawString('Cleaner', $titleFont, $orangeBrush, 84 + $fiveMWidth, 135)

$subtitleFont = [System.Drawing.Font]::new('Segoe UI', 17, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$bodyFont = [System.Drawing.Font]::new('Segoe UI', 13, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Point)
$mutedBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 166, 170, 179))
$bodyBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 219, 221, 225))
$heroGraphics.DrawString('optimizer for FiveM', $subtitleFont, $mutedBrush, 88, 215)
$heroLine = "Tr$([char]0x00EA)s perfis. Altera$([char]0x00E7)$([char]0x00F5)es transparentes. Rollback de verdade."
$separator = "     $([char]0x2022)     "
$heroGraphics.DrawString($heroLine, $bodyFont, $bodyBrush, 88, 275)
$heroGraphics.DrawString("LEVE${separator}EQUILIBRADO${separator}AGRESSIVO", $badgeFont, $mutedBrush, 88, 334)

$heroGraphics.DrawImage($appIcon, [System.Drawing.Rectangle]::new(835, 50, 360, 360))
$hero.Save((Join-Path $docsDirectory 'hero.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$bodyBrush.Dispose()
$mutedBrush.Dispose()
$orangeBrush.Dispose()
$whiteBrush.Dispose()
$bodyFont.Dispose()
$subtitleFont.Dispose()
$titleFont.Dispose()
$badgeFont.Dispose()
$badgeBrush.Dispose()
$badgePath.Dispose()
$heroBackground.Dispose()
$heroGraphics.Dispose()
$hero.Dispose()
$docsIcon.Dispose()
$appIcon.Dispose()
$master.Dispose()

Write-Host "Official FiveMCleaner assets generated from $masterPath"
