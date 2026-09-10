[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$LicensePath,

    [Parameter(Mandatory)]
    [string]$EnglishInfoPath,

    [Parameter(Mandatory)]
    [string]$PortugueseInfoPath,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-RtfEscapedText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Text.ToCharArray()) {
        $codePoint = [int][char]$character
        switch ($character) {
            '\' { [void]$builder.Append('\\') }
            '{' { [void]$builder.Append('\{') }
            '}' { [void]$builder.Append('\}') }
            default {
                if ($codePoint -ge 32 -and $codePoint -le 126) {
                    [void]$builder.Append($character)
                }
                else {
                    if ($codePoint -gt 32767) {
                        $codePoint -= 65536
                    }
                    [void]$builder.Append("\u${codePoint}?")
                }
            }
        }
    }
    return $builder.ToString()
}

function ConvertTo-RtfLinkedText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $builder = [System.Text.StringBuilder]::new()
    $offset = 0
    foreach ($match in [regex]::Matches($Text, 'https://[^\s]+')) {
        [void]$builder.Append((ConvertTo-RtfEscapedText $Text.Substring($offset, $match.Index - $offset)))
        $url = ConvertTo-RtfEscapedText $match.Value
        [void]$builder.Append('{\field{\*\fldinst HYPERLINK "')
        [void]$builder.Append($url)
        [void]$builder.Append('"}{\fldrslt {\ul ')
        [void]$builder.Append($url)
        [void]$builder.Append('}}}')
        $offset = $match.Index + $match.Length
    }
    [void]$builder.Append((ConvertTo-RtfEscapedText $Text.Substring($offset)))
    return $builder.ToString()
}

function ConvertTo-RtfInline {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $builder = [System.Text.StringBuilder]::new()
    $bold = $false
    foreach ($part in [regex]::Split($Text, '(\*\*)')) {
        if ($part -eq '**') {
            $bold = -not $bold
            [void]$builder.Append($(if ($bold) { '\b ' } else { '\b0 ' }))
        }
        else {
            [void]$builder.Append((ConvertTo-RtfLinkedText $part))
        }
    }
    if ($bold) {
        [void]$builder.Append('\b0 ')
    }
    return $builder.ToString()
}

function Get-DocumentBlocks {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $lines = @($Text -split "\r?\n")
    $blocks = [System.Collections.Generic.List[object]]::new()
    $index = 0
    while ($index -lt $lines.Count) {
        $line = $lines[$index].TrimEnd()
        if ([string]::IsNullOrWhiteSpace($line)) {
            $index++
            continue
        }

        if ($line -match '^(#{1,6})\s+(.+)$') {
            $blocks.Add([pscustomobject]@{
                Type = 'Heading'
                Level = $Matches[1].Length
                Text = $Matches[2]
            })
            $index++
            continue
        }

        if ($line -match '^-\s+(.+)$') {
            $text = $Matches[1].Trim()
            $index++
            while ($index -lt $lines.Count -and
                -not [string]::IsNullOrWhiteSpace($lines[$index]) -and
                $lines[$index] -notmatch '^#{1,6}\s+' -and
                $lines[$index] -notmatch '^-\s+') {
                $text += ' ' + $lines[$index].Trim()
                $index++
            }
            $blocks.Add([pscustomobject]@{ Type = 'Bullet'; Level = 0; Text = $text })
            continue
        }

        if ($line -match '^\s{2,}\S') {
            $blocks.Add([pscustomobject]@{ Type = 'Code'; Level = 0; Text = $line.Trim() })
            $index++
            continue
        }

        $text = $line.Trim()
        $index++
        while ($index -lt $lines.Count -and
            -not [string]::IsNullOrWhiteSpace($lines[$index]) -and
            $lines[$index] -notmatch '^#{1,6}\s+' -and
            $lines[$index] -notmatch '^-\s+') {
            $text += ' ' + $lines[$index].Trim()
            $index++
        }
        $blocks.Add([pscustomobject]@{ Type = 'Paragraph'; Level = 0; Text = $text })
    }
    return $blocks
}

function ConvertTo-RtfDocument {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1\fmodern\fcharset0 Consolas;}}\viewkind4\uc1')
    foreach ($block in Get-DocumentBlocks $Text) {
        $content = ConvertTo-RtfInline $block.Text
        switch ($block.Type) {
            'Heading' {
                if ($block.Level -eq 1) {
                    [void]$builder.Append("\pard\keepn\sa260\b\f0\fs34 $content\b0\par`n")
                }
                else {
                    [void]$builder.Append("\pard\keepn\sb180\sa100\b\f0\fs26 $content\b0\par`n")
                }
            }
            'Bullet' {
                [void]$builder.Append("\pard\li360\fi-180\sa80\f0\fs22 \u8226?\tab $content\par`n")
            }
            'Code' {
                [void]$builder.Append("\pard\li180\sa100\f1\fs19 $content\par`n")
            }
            default {
                [void]$builder.Append("\pard\sa140\sl264\slmult1\f0\fs22 $content\par`n")
            }
        }
    }
    [void]$builder.Append('}')
    return $builder.ToString()
}

function Normalize-SemanticText {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    return (($Text -replace '\*\*', '' -replace '\u2022', ' ' -replace '\s+', ' ').Trim())
}

function Write-VerifiedRtfDocument {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $source = Get-Content -LiteralPath $SourcePath -Raw
    $blocks = @(Get-DocumentBlocks $source)
    $rtf = ConvertTo-RtfDocument $source
    [System.IO.File]::WriteAllText($DestinationPath, $rtf, [System.Text.Encoding]::ASCII)

    $viewer = [System.Windows.Forms.RichTextBox]::new()
    try {
        $viewer.Rtf = $rtf
        $expected = Normalize-SemanticText (($blocks | ForEach-Object Text) -join ' ')
        $actual = Normalize-SemanticText $viewer.Text
        if ($actual -cne $expected) {
            throw "Generated installer document changed the source text: $SourcePath"
        }
    }
    finally {
        $viewer.Dispose()
    }

    foreach ($urlMatch in [regex]::Matches($source, 'https://[^\s]+')) {
        $url = $urlMatch.Value
        if (-not $rtf.Contains($url, [StringComparison]::Ordinal)) {
            throw "Generated installer document lost a link target: $url"
        }
    }
}

Add-Type -AssemblyName System.Windows.Forms

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$documents = @(
    @{ Source = $LicensePath; Destination = 'license.rtf' },
    @{ Source = $EnglishInfoPath; Destination = 'install-info.en.rtf' },
    @{ Source = $PortugueseInfoPath; Destination = 'install-info.pt-BR.rtf' }
)

foreach ($document in $documents) {
    $source = [System.IO.Path]::GetFullPath($document.Source)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Installer document source was not found: $source"
    }
    Write-VerifiedRtfDocument -SourcePath $source -DestinationPath (Join-Path $resolvedOutput $document.Destination)
}

Write-Host "Installer documents ready: $resolvedOutput" -ForegroundColor Green
