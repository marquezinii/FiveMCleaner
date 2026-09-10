[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$ChangelogPath = (Join-Path $PSScriptRoot '..\CHANGELOG.md'),

    [Parameter(Mandatory)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$changelog = Get-Content -LiteralPath $ChangelogPath -Raw -Encoding utf8
$escapedVersion = [regex]::Escape($Version)
$release = [regex]::Match($changelog, "(?ms)^## \[$escapedVersion\][^\r\n]*\r?\n(?<body>.*?)(?=^## \[|\z)")
if (-not $release.Success) {
    throw "CHANGELOG.md does not contain an entry for $Version."
}

$headings = [ordered]@{
    'Adicionado' = '## ✨ Novidades'
    'Melhorado' = '## 🔧 Melhorias'
    'Corrigido' = '## 🐛 Correções'
    'Segurança' = '## 🔒 Segurança'
    'Alterações técnicas' = '## ⚙️ Alterações técnicas'
}
$headingOrder = @($headings.Keys)
$body = $release.Groups['body'].Value
$matches = @([regex]::Matches($body, '(?m)^### (?<name>[^\r\n]+)\r?$'))
if ($matches.Count -eq 0) {
    throw "CHANGELOG.md entry $Version has no supported release sections."
}
if (-not [string]::IsNullOrWhiteSpace($body.Substring(0, $matches[0].Index))) {
    throw "CHANGELOG.md entry $Version contains content before its first release section."
}

$result = [Collections.Generic.List[string]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$lastOrder = -1
for ($index = 0; $index -lt $matches.Count; $index++) {
    $match = $matches[$index]
    $name = $match.Groups['name'].Value.Trim()
    if (-not $headings.Contains($name)) {
        throw "CHANGELOG.md entry $Version uses unsupported section '$name'."
    }
    if (-not $seen.Add($name)) {
        throw "CHANGELOG.md entry $Version repeats section '$name'."
    }

    $order = [Array]::IndexOf($headingOrder, $name)
    if ($order -le $lastOrder) {
        throw "CHANGELOG.md entry $Version has release sections out of order."
    }
    $lastOrder = $order

    $contentStart = $match.Index + $match.Length
    $contentEnd = if ($index + 1 -lt $matches.Count) { $matches[$index + 1].Index } else { $body.Length }
    $content = $body.Substring($contentStart, $contentEnd - $contentStart).Trim()
    if ($content -notmatch '(?m)^- \S') {
        throw "CHANGELOG.md entry $Version section '$name' must contain at least one bullet."
    }

    $result.Add("$($headings[$name])`n`n$content")
}

$notes = ($result -join "`n`n").Trim()
if ($notes -match '(?i)@(everyone|here)') {
    throw 'Release notes must not contain Discord-wide mentions.'
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $resolvedOutput
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$notes | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
Write-Host "Release notes ready: $resolvedOutput" -ForegroundColor Green
