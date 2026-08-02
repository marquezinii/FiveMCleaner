import { validateBatch } from './validateEvent.js';
import { validateBugReport } from './bugReports/validateSubmission.js';
import { recentBugReports } from './bugReports/queries.js';
import { validateUpdaterEvent } from './updaterEvents/validateSubmission.js';
import { recentUpdaterEvents } from './updaterEvents/queries.js';
import { createPasswordAuthProvider } from './auth/passwordAuthProvider.js';
import * as queries from './stats/queries.js';
import { toCsv } from './stats/csv.js';
import { buildCorsHeaders, isAllowedDashboardOrigin, withCorsHeaders } from './cors.js';
import { readBoundedJson } from './requestSecurity.js';

const MAX_TELEMETRY_BODY_BYTES = 512 * 1024;
const MAX_BUG_REPORT_BODY_BYTES = 128 * 1024;
const MAX_UPDATER_EVENT_BODY_BYTES = 4 * 1024;

// FiveMCleaner anonymous telemetry + bug reports + admin dashboard API
// Worker. See wrangler.toml and README.md for deployment status of each
// route. Bug reports are text-only -- no attachment/screenshot support, no
// R2 dependency -- everything lives in D1.
//
// Routes:
//   POST    /telemetry             -- ingest a batch of telemetry events (no auth; validated server-side)
//   POST    /bugs                  -- ingest one bug report, text-only (no auth; validated server-side)
//   POST    /admin/login           -- { password } -> session cookie
//   POST    /admin/logout          -- clears the session cookie
//   GET     /api/stats/:name       -- one chart's data (requires a valid session)
//   GET     /api/stats/:name.csv   -- same data as CSV (requires a valid session)
//   GET     /api/bugs              -- recent bug reports, newest first (requires a valid session)
//   OPTIONS *                      -- CORS preflight for the routes above
//
// The dashboard is served from a different origin than this Worker (a
// Cloudflare Pages domain, or a different localhost port while testing
// locally), so every response carries CORS headers scoped to exactly the
// single origin configured in the DASHBOARD_ORIGIN var -- see cors.js.

const STATS_BUILDERS = {
  'runs-per-day': queries.optimizationRunsPerDay,
  'os-versions': queries.osVersionBreakdown,
  'app-versions': queries.appVersionBreakdown,
  'top-actions': queries.topActions,
  'average-time': queries.averageOptimizationTimeMs,
  'success-rate': queries.successRate,
  'errors-by-version': queries.errorsByVersion,
  'error-categories': queries.errorCategoryBreakdown,
  'top-actions-in-failures': queries.topActionsInFailures,
  'recent-failures': queries.recentFailures,
  'top-cpu': queries.topCpuModels,
  'top-gpu': queries.topGpuModels,
  'ram-buckets': queries.ramBucketBreakdown,
  profiles: queries.profileBreakdown,
};

// D1's batch() rejects calls with more than 500 statements. A single
// telemetry batch is capped at MAX_BATCH_SIZE=50 events, each carrying up to
// MAX_ACTION_IDS=30 action ids, so the action-link statements alone can reach
// 1500 -- well over the limit. Chunking keeps every batch call inside the
// bound and avoids a 500 + partial write on oversized payloads.
export const MAX_D1_BATCH_STATEMENTS = 500;

export function chunkStatements(statements, maxStatements = MAX_D1_BATCH_STATEMENTS) {
  const chunks = [];
  for (let i = 0; i < statements.length; i += maxStatements) {
    chunks.push(statements.slice(i, i + maxStatements));
  }
  return chunks;
}


export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const corsHeaders = buildCorsHeaders(request.headers.get('Origin'), env.DASHBOARD_ORIGIN);

    if (request.method === 'OPTIONS') {
      return new Response(null, { status: 204, headers: corsHeaders });
    }

    const response = await route(request, env, url);
    return withCorsHeaders(response, corsHeaders);
  },
};

async function route(request, env, url) {
  if (request.method === 'POST' && url.pathname === '/telemetry') {
    return handleTelemetryIngest(request, env);
  }

  if (request.method === 'POST' && url.pathname === '/bugs') {
    return handleBugReportIngest(request, env);
  }
  if (request.method === 'POST' && url.pathname === '/updater-events') {
    return handleUpdaterEventIngest(request, env);
  }
  if (request.method === 'GET' && url.pathname === '/update/manifest') {
    return handleSignedReleaseManifest(env);
  }

  if (request.method === 'POST' && url.pathname === '/admin/login') {
    if (!isAllowedDashboardOrigin(request.headers.get('Origin'), env.DASHBOARD_ORIGIN)) {
      return new Response('Forbidden', { status: 403 });
    }
    return createPasswordAuthProvider(env).login(request);
  }

  if (request.method === 'POST' && url.pathname === '/admin/logout') {
    if (!isAllowedDashboardOrigin(request.headers.get('Origin'), env.DASHBOARD_ORIGIN)) {
      return new Response('Forbidden', { status: 403 });
    }
    return createPasswordAuthProvider(env).logout(request);
  }

  if (request.method === 'GET' && url.pathname.startsWith('/api/stats/')) {
    return handleStatsRequest(request, env, url);
  }

  if (request.method === 'GET' && url.pathname === '/api/bugs') {
    return handleBugReportsList(request, env, url);
  }
  if (request.method === 'GET' && url.pathname === '/api/updater-events') {
    return handleUpdaterEventsList(request, env, url);
  }

  return new Response('Not found', { status: 404 });
}

function handleSignedReleaseManifest(env) {
  const manifest = env.RELEASE_MANIFEST_JSON;
  if (typeof manifest !== 'string' || manifest.length === 0) {
    return new Response('Release manifest unavailable', {
      status: 503,
      headers: { 'Cache-Control': 'no-store' },
    });
  }
  if (manifest.length > 65_536) {
    return new Response('Release manifest invalid', { status: 500 });
  }
  let parsed;
  try { parsed = JSON.parse(manifest); } catch {
    return new Response('Release manifest invalid', { status: 500 });
  }
  const keys = parsed && typeof parsed === 'object' && !Array.isArray(parsed)
    ? Object.keys(parsed)
    : [];
  const allowed = ['channel', 'version', 'minimumAllowedVersion', 'packageUrl',
    'packageSha256', 'packageSizeBytes', 'signatureBase64'];
  if (keys.length !== allowed.length || keys.some((key) => !allowed.includes(key))
      || parsed.channel !== 'stable'
      || !/^\d+\.\d+\.\d+$/.test(parsed.version)
      || !/^\d+\.\d+\.\d+$/.test(parsed.minimumAllowedVersion)
      || typeof parsed.packageUrl !== 'string'
      || !/^https:\/\/github\.com\/marquezinii\/FiveMCleaner\/releases\/download\//.test(parsed.packageUrl)
      || !/^[a-f0-9]{64}$/i.test(parsed.packageSha256)
      || !Number.isSafeInteger(parsed.packageSizeBytes) || parsed.packageSizeBytes <= 0
      || typeof parsed.signatureBase64 !== 'string'
      || parsed.signatureBase64.length < 80 || parsed.signatureBase64.length > 256) {
    return new Response('Release manifest invalid', { status: 500 });
  }
  return new Response(manifest, {
    status: 200,
    headers: {
      'Content-Type': 'application/json; charset=utf-8',
      'Cache-Control': 'no-store',
    },
  });
}

async function handleUpdaterEventIngest(request, env) {
  const payload = await readBoundedJson(request, MAX_UPDATER_EVENT_BODY_BYTES);
  if (payload === null) return new Response('Invalid JSON', { status: 400 });
  const event = validateUpdaterEvent(payload);
  if (event === null) return new Response('Updater event failed validation', { status: 400 });
  await env.TELEMETRY_DB.prepare(
    `INSERT OR IGNORE INTO updater_events
       (event_id, stage, outcome, error_code, previous_version, candidate_version, environment, received_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(event.eventId, event.stage, event.outcome, event.errorCode, event.previousVersion,
    event.candidateVersion, event.environment, new Date().toISOString()).run();
  return new Response(null, { status: 202 });
}

async function handleUpdaterEventsList(request, env, url) {
  const auth = await createPasswordAuthProvider(env).requireSession(request);
  if (!auth.authorized) return auth.response;
  const { sql, params } = recentUpdaterEvents({
    environment: url.searchParams.get('environment') || undefined,
    version: url.searchParams.get('version') || undefined,
  }, url.searchParams.get('limit'));
  const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();
  return new Response(JSON.stringify(results), { status: 200, headers: { 'Content-Type': 'application/json' } });
}

async function handleTelemetryIngest(request, env) {
  const payload = await readBoundedJson(request, MAX_TELEMETRY_BODY_BYTES);
  if (payload === null) return new Response('Invalid JSON', { status: 400 });

  const events = validateBatch(payload);
  if (events === null) {
    return new Response('Event batch failed validation', { status: 400 });
  }

  const receivedAt = new Date().toISOString();
  const statements = [];
  for (const event of events) {
    statements.push(
      env.TELEMETRY_DB
        .prepare(
          `INSERT INTO telemetry_events
             (event_name, execution_time_ms, app_version, error_category,
              os_version, system_architecture, cpu_model, gpu_model,
              ram_bucket_gib, profile, environment, received_at)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
        )
        .bind(
          event.eventName,
          event.executionTimeMs,
          event.appVersion,
          event.errorCategory,
          event.osVersion,
          event.systemArchitecture,
          event.cpuModel,
          event.gpuModel,
          event.ramBucketGiB,
          event.profile,
          event.environment,
          receivedAt,
        ),
    );
  }

  const results = [];
  for (const chunk of chunkStatements(statements)) {
    results.push(...await env.TELEMETRY_DB.batch(chunk));
  }

  const actionStatements = [];
  results.forEach((result, index) => {
    const eventId = result.meta?.last_row_id;
    if (!eventId) {
      return;
    }

    for (const actionId of events[index].actionIds) {
      actionStatements.push(
        env.TELEMETRY_DB
          .prepare('INSERT INTO telemetry_event_actions (telemetry_event_id, action_id) VALUES (?, ?)')
          .bind(eventId, actionId),
      );
    }
  });

  for (const chunk of chunkStatements(actionStatements)) {
    await env.TELEMETRY_DB.batch(chunk);
  }

  return new Response(null, { status: 202 });
}

async function handleStatsRequest(request, env, url) {
  const auth = await createPasswordAuthProvider(env).requireSession(request);
  if (!auth.authorized) {
    return auth.response;
  }

  const asCsv = url.pathname.endsWith('.csv');
  const name = url.pathname
    .slice('/api/stats/'.length)
    .replace(/\.csv$/, '');

  const builder = STATS_BUILDERS[name];
  if (!builder) {
    return new Response('Unknown stat', { status: 404 });
  }

  const filters = {
    from: url.searchParams.get('from') || undefined,
    to: url.searchParams.get('to') || undefined,
    appVersion: url.searchParams.get('version') || undefined,
    environment: url.searchParams.get('environment') || undefined,
  };

  const { sql, params } = builder(filters);
  const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();

  if (asCsv) {
    return new Response(toCsv(results), {
      status: 200,
      headers: {
        'Content-Type': 'text/csv; charset=utf-8',
        'Content-Disposition': `attachment; filename="${name}.csv"`,
      },
    });
  }

  return new Response(JSON.stringify(results), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}

async function handleBugReportIngest(request, env) {
  const payload = await readBoundedJson(request, MAX_BUG_REPORT_BODY_BYTES);
  if (payload === null) return new Response('Invalid JSON', { status: 400 });

  const report = validateBugReport(payload);
  if (report === null) {
    return new Response('Bug report failed validation', { status: 400 });
  }

  await env.TELEMETRY_DB
    .prepare(
      `INSERT OR IGNORE INTO bug_reports
         (report_id, category, summary, description, app_version, profile,
          technical_summary, email, log_text, environment, received_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    )
    .bind(
      report.reportId,
      report.category,
      report.summary,
      report.description,
      report.appVersion,
      report.profile,
      report.technicalSummary,
      report.email,
      report.logText,
      report.environment,
      new Date().toISOString(),
    )
    .run();

  return new Response(JSON.stringify({ success: true }), {
    status: 202,
    headers: { 'Content-Type': 'application/json' },
  });
}

async function handleBugReportsList(request, env, url) {
  const auth = await createPasswordAuthProvider(env).requireSession(request);
  if (!auth.authorized) {
    return auth.response;
  }

  const filters = {
    environment: url.searchParams.get('environment') || undefined,
    category: url.searchParams.get('category') || undefined,
    from: url.searchParams.get('from') || undefined,
    to: url.searchParams.get('to') || undefined,
  };
  const limit = Number(url.searchParams.get('limit')) || undefined;

  const { sql, params } = recentBugReports(filters, limit);
  const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();

  return new Response(JSON.stringify(results), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  });
}
