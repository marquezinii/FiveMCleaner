[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-VersionParts {
    param([Parameter(Mandatory)][string]$Value)

    $parts = $Value.Split('.')
    return [pscustomobject]@{
        Major = [int64]$parts[0]
        Minor = [int64]$parts[1]
        Patch = [int64]$parts[2]
    }
}

$candidate = ConvertTo-VersionParts $Version
$stableTags = @(git tag --merged HEAD --list 'v*' | ForEach-Object {
    if ($_ -match '^v(?<version>\d+\.\d+\.\d+)$') {
        [pscustomobject]@{ Tag = $_; Parts = ConvertTo-VersionParts $Matches.version }
    }
})

if ($stableTags.Count -eq 0) {
    Write-Host "No previous stable tag found; '$Version' is accepted as the initial public version."
    exit 0
}

$previous = $stableTags |
    Sort-Object -Property @{ Expression = { $_.Parts.Major }; Descending = $true },
                           @{ Expression = { $_.Parts.Minor }; Descending = $true },
                           @{ Expression = { $_.Parts.Patch }; Descending = $true } |
    Select-Object -First 1

if ($previous.Tag -eq "v$Version") {
    Write-Host "Version '$Version' already matches the current stable tag."
    exit 0
}

# The product intentionally starts its public sequence at 1.0.0 after the
# pre-public 0.x line. All later stable versions must follow the policy below.
if ($previous.Tag -eq 'v0.2.0' -and $Version -eq '1.0.0') {
    Write-Host "Public version bootstrap from $($previous.Tag) to v1.0.0 accepted."
    exit 0
}

$expected = if ($previous.Parts.Patch -lt 99) {
    "{0}.{1}.{2}" -f $previous.Parts.Major, $previous.Parts.Minor, ($previous.Parts.Patch + 1)
}
else {
    "{0}.{1}.0" -f $previous.Parts.Major, ($previous.Parts.Minor + 1)
}

if ($Version -ne $expected) {
    throw "Invalid public version '$Version'. After '$($previous.Tag)', the required next stable version is '$expected'."
}

Write-Host "Public version progression accepted: $($previous.Tag) -> v$Version"
