[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$AppVersion,
    [string]$ProductionConfigPath = (Join-Path $PSScriptRoot '..\src\Ralven.App\Config\appsettings.Production.json'),
    [string]$WorkerDirectory = (Join-Path $PSScriptRoot '..\infra\cloudflare-worker'),
    [string]$DashboardUrl = 'https://ralven-dashboard.pages.dev'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$config = Get-Content -LiteralPath $ProductionConfigPath -Raw | ConvertFrom-Json
if ($config.environment -ne 'Production') {
    throw 'Production diagnostics smoke requires environment=Production.'
}

$telemetryEndpoint = $null
if (-not [Uri]::TryCreate($config.telemetryEndpoint, [UriKind]::Absolute, [ref]$telemetryEndpoint) -or
    $telemetryEndpoint.AbsoluteUri -ne
        'https://api.vemryx.com/telemetry') {
    throw 'Production telemetry endpoint is not allowlisted.'
}

$sentryDsn = $null
if (-not [Uri]::TryCreate($config.sentryDsn, [UriKind]::Absolute, [ref]$sentryDsn) -or
    $sentryDsn.Scheme -ne [Uri]::UriSchemeHttps -or
    -not $sentryDsn.Host.EndsWith('.sentry.io', [StringComparison]::OrdinalIgnoreCase) -or
    [string]::IsNullOrWhiteSpace($sentryDsn.UserInfo) -or
    -not ($sentryDsn.Segments[-1].Trim('/') -as [long])) {
    throw 'Production Sentry DSN is invalid.'
}

function Invoke-RemoteD1Json([string]$Sql) {
    Push-Location $WorkerDirectory
    try {
        $singleLineSql = ($Sql -replace '[\r\n]+', ' ').Trim()
        $output = npx.cmd wrangler d1 execute TELEMETRY_DB `
            --remote --command $singleLineSql --json
        if ($LASTEXITCODE -ne 0) {
            throw 'Remote D1 command failed.'
        }
        return $output | ConvertFrom-Json
    }
    finally {
        Pop-Location
    }
}

function Invoke-RemoteD1([string]$Sql) {
    Push-Location $WorkerDirectory
    try {
        $singleLineSql = ($Sql -replace '[\r\n]+', ' ').Trim()
        npx.cmd wrangler d1 execute TELEMETRY_DB `
            --remote --command $singleLineSql | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw 'Remote D1 command failed.'
        }
    }
    finally {
        Pop-Location
    }
}

$schemaResult = Invoke-RemoteD1Json @"
SELECT
  EXISTS(SELECT 1 FROM pragma_table_info('telemetry_events') WHERE name = 'event_id') AS has_event_id,
  EXISTS(SELECT 1 FROM pragma_table_info('telemetry_events') WHERE name = 'bug_code') AS has_bug_code;
"@
$schema = @(@($schemaResult)[0].results)[0]
if (-not $schema.has_event_id -or -not $schema.has_bug_code) {
    throw 'Production D1 is older than the telemetry contract; no synthetic event was sent.'
}

$dashboardResponse = Invoke-WebRequest -Uri $DashboardUrl -Method Get -SkipHttpErrorCheck
if ($dashboardResponse.StatusCode -ne 200) {
    throw "Dashboard health check returned HTTP $($dashboardResponse.StatusCode)."
}

$eventId = [Guid]::NewGuid().ToString()
$actionId = 'release.telemetry.smoke'
$payload = @{
    eventId = $eventId
    eventName = 'optimization-failed'
    executionTimeMs = 1
    appVersion = $AppVersion
    errorCategory = 'unexpected'
    bugCode = 'NET_TELEMETRY_DELIVERY'
    environment = 'Production'
    cpuModel = 'Ralven release smoke'
    profile = 'Light'
    actionIds = @($actionId)
} | ConvertTo-Json -Compress

try {
    $response = Invoke-WebRequest -Uri $telemetryEndpoint -Method Post `
        -ContentType 'application/json' -Body $payload -SkipHttpErrorCheck
    if ($response.StatusCode -ne 202) {
        throw "Telemetry ingest returned HTTP $($response.StatusCode)."
    }

    $query = @"
SELECT e.event_id, e.app_version, e.bug_code, e.environment, e.cpu_model, a.action_id
FROM telemetry_events e
JOIN telemetry_event_actions a ON a.telemetry_event_id = e.id
WHERE e.event_id = '$eventId' AND a.action_id = '$actionId';
"@
    $queryResult = Invoke-RemoteD1Json $query
    $row = @(@($queryResult)[0].results)[0]
    if ($null -eq $row -or
        $row.event_id -ne $eventId -or
        $row.app_version -ne $AppVersion -or
        $row.bug_code -ne 'NET_TELEMETRY_DELIVERY' -or
        $row.environment -ne 'Production' -or
        $row.cpu_model -ne 'Ralven release smoke' -or
        $row.action_id -ne $actionId) {
        throw 'Telemetry was accepted but did not reach the dashboard D1 contract intact.'
    }
}
finally {
    $cleanup = @"
DELETE FROM telemetry_event_actions
WHERE telemetry_event_id IN (SELECT id FROM telemetry_events WHERE event_id = '$eventId');
DELETE FROM telemetry_events WHERE event_id = '$eventId';
"@
    Invoke-RemoteD1 $cleanup
    $remainingResult = Invoke-RemoteD1Json `
        "SELECT COUNT(*) AS remaining FROM telemetry_events WHERE event_id = '$eventId';"
    $remaining = @(@($remainingResult)[0].results)[0].remaining
    if ([int]$remaining -ne 0) {
        throw 'Synthetic telemetry cleanup failed.'
    }
}

$sentryEventId = [Guid]::NewGuid().ToString('N')
$sentryEvent = @{
    event_id = $sentryEventId
    timestamp = [DateTimeOffset]::UtcNow.ToString('O')
    platform = 'csharp'
    level = 'error'
    environment = 'Production'
    release = "ralven@$AppVersion"
    logger = 'Ralven.ReleaseDiagnosticsSmoke'
    tags = @{ 'ralven.release_smoke' = 'true' }
    exception = @{
        values = @(@{
            type = 'RalvenReleaseDiagnosticsSmokeException'
            value = 'Synthetic release-gate event without user data.'
            mechanism = @{ type = 'release-smoke'; handled = $true }
        })
    }
} | ConvertTo-Json -Depth 8 -Compress
$eventLength = [Text.Encoding]::UTF8.GetByteCount($sentryEvent)
$envelopeHeader = @{ event_id = $sentryEventId; dsn = $config.sentryDsn } |
    ConvertTo-Json -Compress
$itemHeader = @{ type = 'event'; length = $eventLength } | ConvertTo-Json -Compress
$envelope = "$envelopeHeader`n$itemHeader`n$sentryEvent"
$projectId = $sentryDsn.Segments[-1].Trim('/')
$sentryEndpoint = "https://$($sentryDsn.Host)/api/$projectId/envelope/"
$sentryResponse = Invoke-WebRequest -Uri $sentryEndpoint -Method Post `
    -ContentType 'application/x-sentry-envelope' `
    -Body ([Text.Encoding]::UTF8.GetBytes($envelope)) -SkipHttpErrorCheck
if ($sentryResponse.StatusCode -ne 200) {
    throw "Sentry ingest returned HTTP $($sentryResponse.StatusCode)."
}
$sentryReceipt = $sentryResponse.Content | ConvertFrom-Json
if ($sentryReceipt.id -ne $sentryEventId) {
    throw 'Sentry did not acknowledge the synthetic crash event ID.'
}

Write-Host "Production diagnostics smoke: OK (telemetry $eventId cleaned; Sentry $sentryEventId accepted)." `
    -ForegroundColor Green
