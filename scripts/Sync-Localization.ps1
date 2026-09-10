[CmdletBinding()]
param(
    [ValidateSet('Check', 'Sync', 'Export', 'Import', 'Approve')]
    [string]$Mode = 'Check',

    [string]$TranslationFile = 'artifacts/localization/translation-drafts.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workspace = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$config = Get-Content -LiteralPath (Join-Path $workspace 'localization\locales.json') -Raw | ConvertFrom-Json
$glossary = Get-Content -LiteralPath (Join-Path $workspace 'localization\glossary.json') -Raw | ConvertFrom-Json
$reviewStatePath = Join-Path $workspace 'localization\review-state.json'
$reviewState = if (Test-Path -LiteralPath $reviewStatePath -PathType Leaf) {
    Get-Content -LiteralPath $reviewStatePath -Raw | ConvertFrom-Json -AsHashtable
} else {
    @{}
}
$sourceLanguage = @($config.languages | Where-Object culture -eq $config.sourceCulture)
if ($sourceLanguage.Count -ne 1) {
    throw 'localization/locales.json must define exactly one source culture.'
}

function Resolve-CatalogPath([object]$language, [object]$resourceSet) {
    $relativePath = [string]$language.($resourceSet.pathProperty)
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        throw "Missing $($resourceSet.pathProperty) for $($language.culture)."
    }
    [System.IO.Path]::GetFullPath((Join-Path $workspace $relativePath))
}

function Load-Catalog([string]$path) {
    [System.Xml.Linq.XDocument]::Load($path, [System.Xml.Linq.LoadOptions]::PreserveWhitespace)
}

function Get-DataElements([System.Xml.Linq.XDocument]$document) {
    @($document.Descendants('data'))
}

function Get-Key([System.Xml.Linq.XElement]$element) {
    [string]$element.Attribute('name').Value
}

function Get-Value([System.Xml.Linq.XElement]$element) {
    [string]$element.Element('value').Value
}

function Get-PlaceholderSignature([string]$value) {
    @([regex]::Matches(
        $value,
        '(?<!\{)\{\d+(?:,[^}:]+)?(?:\:[^}]+)?\}(?!\})') |
        ForEach-Object Value |
        Sort-Object) -join '|'
}

function Get-SourceHash([string]$value) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($value)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-ReviewedHash([string]$resourceSet, [string]$key) {
    if (-not $reviewState.ContainsKey($resourceSet) -or
        -not $reviewState[$resourceSet].ContainsKey($key)) {
        return $null
    }
    [string]$reviewState[$resourceSet][$key]
}

function Save-Catalog([System.Xml.Linq.XDocument]$document, [string]$path) {
    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $false
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::None
    $writer = [System.Xml.XmlWriter]::Create($path, $settings)
    try { $document.Save($writer) } finally { $writer.Dispose() }
}

function Get-CatalogState([object]$resourceSet) {
    $sourcePath = Resolve-CatalogPath $sourceLanguage[0] $resourceSet
    $sourceDocument = Load-Catalog $sourcePath
    $sourceElements = Get-DataElements $sourceDocument
    $sourceByKey = @{}
    $sourceHashes = @{}
    foreach ($element in $sourceElements) {
        $key = Get-Key $element
        $sourceByKey[$key] = $element
        $sourceHashes[$key] = Get-SourceHash (Get-Value $element)
    }
    [pscustomobject]@{
        Set = $resourceSet
        SourcePath = $sourcePath
        SourceDocument = $sourceDocument
        SourceElements = $sourceElements
        SourceByKey = $sourceByKey
        SourceHashes = $sourceHashes
    }
}

$states = @($config.resourceSets | ForEach-Object { Get-CatalogState $_ })

if ($Mode -eq 'Sync') {
    foreach ($state in $states) {
        foreach ($language in @($config.languages | Where-Object culture -ne $config.sourceCulture)) {
            $path = Resolve-CatalogPath $language $state.Set
            $changed = $false
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                $document = Load-Catalog $path
            } else {
                $document = Load-Catalog $state.SourcePath
                $changed = $true
                foreach ($element in Get-DataElements $document) {
                    if ((Get-Key $element) -notin @($state.Set.allowEmptyKeys)) {
                        $element.Add([System.Xml.Linq.XElement]::new(
                            'comment',
                            "TODO(i18n): translate from $($config.sourceCulture)"))
                    }
                }
            }

            $targetByKey = @{}
            foreach ($element in Get-DataElements $document) {
                $targetByKey[(Get-Key $element)] = $element
            }
            foreach ($sourceElement in $state.SourceElements) {
                $key = Get-Key $sourceElement
                if (-not $targetByKey.ContainsKey($key)) {
                    $copy = [System.Xml.Linq.XElement]::new($sourceElement)
                    $copy.Add([System.Xml.Linq.XElement]::new(
                        'comment',
                        "TODO(i18n): translate from $($config.sourceCulture)"))
                    $document.Root.Add([System.Xml.Linq.XText]::new("`n  "))
                    $document.Root.Add($copy)
                    $changed = $true
                    continue
                }
                if ((Get-ReviewedHash $state.Set.name $key) -ne $state.SourceHashes[$key] -and
                    $null -eq $targetByKey[$key].Element('comment')) {
                    $targetByKey[$key].Add([System.Xml.Linq.XElement]::new(
                        'comment',
                        "TODO(i18n): source changed in $($config.sourceCulture)"))
                    $changed = $true
                }
            }
            if ($changed) {
                Save-Catalog $document $path
                Write-Host "Synchronized $($state.Set.name)/$($language.culture): $path"
            }
        }
    }
}

if ($Mode -eq 'Export' -or $Mode -eq 'Sync') {
    $drafts = [System.Collections.Generic.List[object]]::new()
    foreach ($state in $states) {
        foreach ($language in @($config.languages | Where-Object culture -ne $config.sourceCulture)) {
            $path = Resolve-CatalogPath $language $state.Set
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
            foreach ($element in Get-DataElements (Load-Catalog $path)) {
                $comment = $element.Element('comment')
                if ($null -eq $comment -or $comment.Value -notlike 'TODO(i18n):*') { continue }
                $key = Get-Key $element
                $drafts.Add([ordered]@{
                    resourceSet = $state.Set.name
                    culture = $language.culture
                    key = $key
                    source = Get-Value $state.SourceByKey[$key]
                    translation = Get-Value $element
                })
            }
        }
    }

    $resolvedTranslationFile = [System.IO.Path]::GetFullPath((Join-Path $workspace $TranslationFile))
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedTranslationFile) | Out-Null
    @($drafts) | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedTranslationFile -Encoding utf8NoBOM
    Write-Host "Exported $($drafts.Count) translation draft(s): $resolvedTranslationFile"
    return
}

if ($Mode -eq 'Import') {
    $resolvedTranslationFile = [System.IO.Path]::GetFullPath((Join-Path $workspace $TranslationFile))
    $translations = @(Get-Content -LiteralPath $resolvedTranslationFile -Raw | ConvertFrom-Json)
    foreach ($state in $states) {
        foreach ($language in @($config.languages | Where-Object culture -ne $config.sourceCulture)) {
            $items = @($translations | Where-Object {
                $_.resourceSet -eq $state.Set.name -and $_.culture -eq $language.culture
            })
            if ($items.Count -eq 0) { continue }
            $path = Resolve-CatalogPath $language $state.Set
            $document = Load-Catalog $path
            $targetByKey = @{}
            foreach ($element in Get-DataElements $document) {
                $targetByKey[(Get-Key $element)] = $element
            }
            foreach ($item in $items) {
                $key = [string]$item.key
                $translation = [string]$item.translation
                if (-not $state.SourceByKey.ContainsKey($key) -or
                    -not $targetByKey.ContainsKey($key) -or
                    [string]::IsNullOrWhiteSpace($translation)) {
                    throw "Invalid translation draft: $($item.resourceSet)/$($item.culture).$key"
                }
                if ((Get-PlaceholderSignature (Get-Value $state.SourceByKey[$key])) -ne
                    (Get-PlaceholderSignature $translation)) {
                    throw "Placeholder mismatch: $($item.resourceSet)/$($item.culture).$key"
                }
                $target = $targetByKey[$key]
                $target.Element('value').Value = $translation
                $comment = $target.Element('comment')
                if ($null -ne $comment -and $comment.Value.StartsWith('TODO(i18n):', [StringComparison]::Ordinal)) {
                    $comment.Remove()
                }
            }
            Save-Catalog $document $path
            Write-Host "Imported $($items.Count) translation(s): $($state.Set.name)/$($language.culture)"
        }
    }
}

$errors = [System.Collections.Generic.List[string]]::new()
foreach ($state in $states) {
    $sourceKeys = @($state.SourceElements | ForEach-Object { Get-Key $_ })
    if ($Mode -ne 'Approve') {
        foreach ($key in $sourceKeys) {
            if ((Get-ReviewedHash $state.Set.name $key) -ne $state.SourceHashes[$key]) {
                $errors.Add("Source needs translation review: $($state.Set.name).$key")
            }
        }
        if ($reviewState.ContainsKey($state.Set.name)) {
            foreach ($key in @($reviewState[$state.Set.name].Keys | Where-Object { $_ -notin $sourceKeys })) {
                $errors.Add("Orphan review state: $($state.Set.name).$key")
            }
        }
    }
    foreach ($language in @($config.languages)) {
        $prefix = "$($state.Set.name)/$($language.culture)"
        $path = Resolve-CatalogPath $language $state.Set
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $errors.Add("Missing catalog: $prefix ($path)")
            continue
        }
        $elements = Get-DataElements (Load-Catalog $path)
        foreach ($duplicate in @($elements | Group-Object { Get-Key $_ } | Where-Object Count -gt 1)) {
            $errors.Add("Duplicate key: $prefix.$($duplicate.Name)")
        }
        $targetByKey = @{}
        foreach ($element in $elements) { $targetByKey[(Get-Key $element)] = $element }
        foreach ($key in $sourceKeys) {
            if (-not $targetByKey.ContainsKey($key)) {
                $errors.Add("Missing key: $prefix.$key")
                continue
            }
            $value = Get-Value $targetByKey[$key]
            if ([string]::IsNullOrWhiteSpace($value) -and $key -notin @($state.Set.allowEmptyKeys)) {
                $errors.Add("Blank translation: $prefix.$key")
            }
            $comment = $targetByKey[$key].Element('comment')
            if ($null -ne $comment -and [string]$comment.Value -like 'TODO(i18n):*') {
                $errors.Add("Unreviewed translation: $prefix.$key")
            }
            $sourceValue = Get-Value $state.SourceByKey[$key]
            if ((Get-PlaceholderSignature $sourceValue) -ne (Get-PlaceholderSignature $value)) {
                $errors.Add("Placeholder mismatch: $prefix.$key")
            }
            foreach ($term in @($glossary.protectedTerms)) {
                $changed = $sourceValue.Contains([string]$term, [StringComparison]::Ordinal) -and
                    -not $value.Contains([string]$term, [StringComparison]::Ordinal)
                if ($changed) {
                    $errors.Add("Protected term changed: $prefix.$key ($term)")
                }
            }
        }
        foreach ($key in @($targetByKey.Keys | Where-Object { $_ -notin $sourceKeys })) {
            $errors.Add("Orphan key: $prefix.$key")
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | Sort-Object | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    throw "Localization validation failed with $($errors.Count) error(s)."
}

if ($Mode -eq 'Approve') {
    $approved = [ordered]@{}
    foreach ($state in $states) {
        $keys = [ordered]@{}
        foreach ($key in @($state.SourceHashes.Keys | Sort-Object)) {
            $keys[$key] = $state.SourceHashes[$key]
        }
        $approved[$state.Set.name] = $keys
    }
    $approved | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reviewStatePath -Encoding utf8NoBOM
    Write-Host "Approved current source text for translation review: $reviewStatePath"
}

$totalKeys = ($states | ForEach-Object SourceElements | Measure-Object).Count
Write-Host "Localization validation passed: $totalKeys keys in $($states.Count) resource set(s), across $(@($config.languages).Count) languages."
