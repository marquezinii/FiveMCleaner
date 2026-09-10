[CmdletBinding()]
param(
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot),
    [string]$PullRequestBaseRef,
    [string]$PullRequestHeadRef,
    [string]$PullRequestTitle,
    [string]$BaseSha,
    [string]$HeadSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Workspace = [IO.Path]::GetFullPath($Workspace)

function Get-ResourceCatalog {
    param([Parameter(Mandatory)][string]$Path)

    [xml]$document = Get-Content -LiteralPath $Path -Raw -Encoding utf8
    $catalog = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($entry in @($document.root.data)) {
        $key = $entry.GetAttribute('name')
        $value = [string]$entry.value
        if (-not $catalog.TryAdd($key, $value)) {
            throw "Duplicate localization key '$key' in '$Path'."
        }
    }

    return $catalog
}

function Get-FormatPlaceholders {
    param([AllowEmptyString()][string]$Value)

    return @([regex]::Matches(
        $Value,
        '(?<!\{)\{[0-9]+(?:,-?[0-9]+)?(?::[^{}]+)?\}(?!\})') |
        ForEach-Object Value |
        Sort-Object -Unique)
}

function Assert-LocalizedText {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Field
    )

    foreach ($culture in @('pt', 'en')) {
        $property = $Value.PSObject.Properties[$culture]
        if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            throw "Public roadmap field '$Field.$culture' must be a non-empty string."
        }
    }
}

$resourceDirectory = Join-Path $Workspace 'src/Ralven.App/Resources'
$resourceFiles = @('Strings.resx', 'Strings.pt-BR.resx', 'Strings.es.resx')
$catalogs = @{}
foreach ($file in $resourceFiles) {
    $catalogs[$file] = Get-ResourceCatalog -Path (Join-Path $resourceDirectory $file)
}

$baseline = $catalogs['Strings.resx']
foreach ($file in @('Strings.pt-BR.resx', 'Strings.es.resx')) {
    $catalog = $catalogs[$file]
    $missing = @($baseline.Keys | Where-Object { -not $catalog.ContainsKey($_) } | Sort-Object)
    $extra = @($catalog.Keys | Where-Object { -not $baseline.ContainsKey($_) } | Sort-Object)
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "Localization catalog '$file' differs from Strings.resx. Missing: $($missing -join ', '); extra: $($extra -join ', ')."
    }

    foreach ($key in $baseline.Keys) {
        if (-not [string]::IsNullOrWhiteSpace($baseline[$key]) -and
            [string]::IsNullOrWhiteSpace($catalog[$key])) {
            throw "Localization key '$key' in '$file' must not be empty."
        }
        $expected = @(Get-FormatPlaceholders -Value $baseline[$key])
        $actual = @(Get-FormatPlaceholders -Value $catalog[$key])
        if ($expected.Count -ne $actual.Count -or
            ($expected.Count -gt 0 -and (Compare-Object -ReferenceObject $expected -DifferenceObject $actual))) {
            throw "Localization placeholders differ for '$key' in '$file'. Expected: $($expected -join ', '); actual: $($actual -join ', ')."
        }
    }
}

$roadmapPath = Join-Path $Workspace 'docs/public-roadmap.json'
$roadmap = Get-Content -LiteralPath $roadmapPath -Raw -Encoding utf8 |
    ConvertFrom-Json -Depth 20
if ($roadmap.version -ne 1) {
    throw 'docs/public-roadmap.json must use schema version 1.'
}
Assert-LocalizedText -Value $roadmap.title -Field 'title'
Assert-LocalizedText -Value $roadmap.description -Field 'description'

$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$items = @($roadmap.items)
if ($items.Count -eq 0) {
    throw 'docs/public-roadmap.json must contain at least one item.'
}
foreach ($item in $items) {
    $id = [string]$item.id
    if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
        throw "Public roadmap item id '$id' must use lower-case kebab-case."
    }
    if (-not $ids.Add($id)) {
        throw "Duplicate public roadmap item id '$id'."
    }
    foreach ($field in @('status', 'title', 'description')) {
        Assert-LocalizedText -Value $item.$field -Field "items[$id].$field"
    }
}

[xml]$props = Get-Content -LiteralPath (Join-Path $Workspace 'Directory.Build.props') -Raw
$version = [string](@($props.Project.PropertyGroup.Version) |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Directory.Build.props must define one numeric SemVer version; found '$version'."
}

$installer = Get-Content -LiteralPath (Join-Path $Workspace 'installer/Ralven.iss') -Raw
if ($installer -match '(?m)^\s*#define\s+App(?:Numeric)?Version\s+"') {
    throw 'installer/Ralven.iss must receive versions from Build-Installer.ps1 instead of carrying a fallback version.'
}
foreach ($required in @('#ifndef AppVersion', '#ifndef AppNumericVersion')) {
    if (-not $installer.Contains($required, [StringComparison]::Ordinal)) {
        throw "installer/Ralven.iss is missing the required version guard '$required'."
    }
}

$nodeVersion = (Get-Content -LiteralPath (Join-Path $Workspace '.node-version') -Raw).Trim()
if ($nodeVersion -notmatch '^(?<major>\d+)\.\d+\.\d+$') {
    throw ".node-version must contain an exact numeric version; found '$nodeVersion'."
}
$expectedEngine = ">=$nodeVersion <$([int]$Matches.major + 1)"
foreach ($directory in @('infra/cloudflare-worker', 'infra/dashboard', 'website')) {
    $package = Get-Content -LiteralPath (Join-Path $Workspace "$directory/package.json") -Raw |
        ConvertFrom-Json -Depth 20
    if ([string]$package.engines.node -ne $expectedEngine) {
        throw "$directory/package.json must use Node engine '$expectedEngine' from .node-version."
    }

    $lock = Get-Content -LiteralPath (Join-Path $Workspace "$directory/package-lock.json") -Raw |
        ConvertFrom-Json -Depth 100 -AsHashtable
    if ([string]$lock['packages']['']['engines']['node'] -ne $expectedEngine) {
        throw "$directory/package-lock.json must use Node engine '$expectedEngine' from .node-version."
    }
}

$sdkVersion = [string](Get-Content -LiteralPath (Join-Path $Workspace 'global.json') -Raw |
    ConvertFrom-Json).sdk.version
$workerPackage = Get-Content -LiteralPath (Join-Path $Workspace 'infra/cloudflare-worker/package.json') -Raw |
    ConvertFrom-Json
$wranglerVersion = [regex]::Match([string]$workerPackage.devDependencies.wrangler, '\d+\.\d+').Value
$readme = Get-Content -LiteralPath (Join-Path $Workspace 'README.md') -Raw
$stackDocumentation = Get-Content -LiteralPath (Join-Path $Workspace 'docs/codebase/STACK.md') -Raw
foreach ($document in @($readme, $stackDocumentation)) {
    if (-not $document.Contains($sdkVersion, [StringComparison]::Ordinal) -or
        -not $document.Contains($nodeVersion, [StringComparison]::Ordinal)) {
        throw 'README.md and docs/codebase/STACK.md must reflect the pinned .NET SDK and Node.js versions.'
    }
}
if (-not $stackDocumentation.Contains("Wrangler $wranglerVersion", [StringComparison]::Ordinal)) {
    throw 'docs/codebase/STACK.md must reflect the pinned Wrangler major/minor version.'
}

$contractCodes = @([regex]::Matches(
    (Get-Content -LiteralPath (Join-Path $Workspace 'src/Ralven.Contracts/BugCode.cs') -Raw),
    '(?m)^\s*(?<code>[A-Z][A-Z0-9_]+)\s*=\s*\d+,?\s*$') |
    ForEach-Object { $_.Groups['code'].Value } |
    Sort-Object -Unique)
$workerCodes = @([regex]::Matches(
    (Get-Content -LiteralPath (Join-Path $Workspace 'infra/cloudflare-worker/src/bugCodes.js') -Raw),
    "'(?<code>[A-Z][A-Z0-9_]+)'") |
    ForEach-Object { $_.Groups['code'].Value } |
    Sort-Object -Unique)
if (Compare-Object -ReferenceObject $contractCodes -DifferenceObject $workerCodes) {
    throw 'The Worker bug-code allowlist must exactly match Ralven.Contracts.BugCode.'
}

$expectedPrefixes = @($contractCodes | ForEach-Object { $_.Split('_')[0] } | Sort-Object -Unique)
$dashboardSource = Get-Content -LiteralPath (Join-Path $Workspace 'infra/dashboard/assets/charts.js') -Raw
$categoryMap = [regex]::Match($dashboardSource, '(?s)const BUG_CODE_CATEGORY_LABELS = \{(?<body>.*?)\};')
if (-not $categoryMap.Success) {
    throw 'The dashboard bug-code category map was not found.'
}
$dashboardPrefixes = @([regex]::Matches($categoryMap.Groups['body'].Value, '(?m)^\s*(?<prefix>[A-Z]+):') |
    ForEach-Object { $_.Groups['prefix'].Value } |
    Sort-Object -Unique)
if (Compare-Object -ReferenceObject $expectedPrefixes -DifferenceObject $dashboardPrefixes) {
    throw 'The dashboard bug-code category map must cover every contract prefix.'
}

$pullRequestValues = @(
    $PullRequestBaseRef,
    $PullRequestHeadRef,
    $PullRequestTitle,
    $BaseSha,
    $HeadSha
)
if ($pullRequestValues | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
    if ($pullRequestValues | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
        throw 'Pull request policy requires base ref, head ref, title, base SHA and head SHA together.'
    }
    if ($PullRequestTitle -notmatch '^(feat|fix|docs|refactor|test|chore|ci|perf|build|revert)(\([a-z0-9_-]+\))?!?: .+') {
        throw "Pull request title must follow Conventional Commits: '$PullRequestTitle'."
    }

    if ($PullRequestBaseRef -eq 'main') {
        if ($PullRequestHeadRef -ne 'dev/proxima-versao') {
            throw "Pull requests to main must originate from dev/proxima-versao, not '$PullRequestHeadRef'."
        }
    }
    elseif ($PullRequestBaseRef -eq 'dev/proxima-versao') {
        $isDependencyBot = $PullRequestHeadRef -match '^(dependabot|renovate)/'
        if (-not $isDependencyBot -and
            $PullRequestHeadRef -notmatch '^(feat|fix|refactor|perf|security|test|docs|chore|task)/[a-z0-9][a-z0-9._-]*$') {
            throw "Task branch '$PullRequestHeadRef' does not follow AI_RULES.md."
        }

        if (-not $isDependencyBot) {
            if ($BaseSha -notmatch '^[0-9a-f]{40}$' -or $HeadSha -notmatch '^[0-9a-f]{40}$') {
                throw 'Pull request policy requires full Git commit SHAs.'
            }
            $changedFiles = @(& git -C $Workspace diff --name-only "$BaseSha...$HeadSha")
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not inspect the pull request diff for OBJECTIVE.md.'
            }
            if ('OBJECTIVE.md' -notin $changedFiles) {
                throw 'Task pull requests must add or update OBJECTIVE.md.'
            }

            $objectivePath = Join-Path $Workspace 'OBJECTIVE.md'
            $objective = Get-Content -LiteralPath $objectivePath -Raw -Encoding utf8
            foreach ($field in @('Agente', 'Objetivo', 'Escopo', 'Critérios de conclusão', 'Resultado entregue')) {
                if ($objective -notmatch "(?m)^- \*\*$([regex]::Escape($field)):\*\*\s+\S") {
                    throw "OBJECTIVE.md must contain a non-empty '$field' field."
                }
            }
            if ($objective -match '(?im)^- \*\*Resultado entregue:\*\*\s+.*\bem andamento\b') {
                throw 'OBJECTIVE.md must describe the delivered result before opening the pull request.'
            }
        }
    }
    else {
        throw "Pull requests must target dev/proxima-versao or main, not '$PullRequestBaseRef'."
    }
}

Write-Host 'Repository policy checks passed.' -ForegroundColor Green
