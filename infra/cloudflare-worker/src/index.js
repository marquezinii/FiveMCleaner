import { validateBatch } from './validateEvent.js';
import { validateBugReport } from './bugReports/validateSubmission.js';
import { recentBugReports } from './bugReports/queries.js';
import { createPasswordAuthProvider } from './auth/passwordAuthProvider.js';
import * as queries from './stats/queries.js';
import { toCsv } from './stats/csv.js';
import { buildCorsHeaders, withCorsHeaders } from './cors.js';

// FiveMCleaner anonymous telemetry + bug reports + admin dashboard API
// Worker. See wrangler.toml and README.md for deployment status of each
// route -- /telemetry is live; /bugs requires a redeploy plus the R2 bucket
// to exist first.
//
// Routes:
//   POST    /telemetry             -- ingest a batch of telemetry events (no auth; validated server-side)
//   POST    /bugs                  -- ingest one bug report + optional screenshot (no auth; validated server-side)
//   POST    /admin/login           -- { password } -> session cookie
//   POST    /admin/logout          -- clears the session cookie
//   GET     /api/stats/:name       -- one chart's data (requires a valid session)
//   GET     /api/stats/:name.csv   -- same data as CSV (requires a valid session)
//   GET     /api/bugs              -- recent bug reports, newest first (requires a valid session)
//   GET     /api/bugs/:id/attachment -- streams a report's screenshot from R2 (requires a valid session)
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

  if (request.method === 'POST' && url.pathname === '/admin/login') {
    return createPasswordAuthProvider(env).login(request);
  }

  if (request.method === 'POST' && url.pathname === '/admin/logout') {
    return createPasswordAuthProvider(env).logout(request);
  }

  if (request.method === 'GET' && url.pathname.startsWith('/api/stats/')) {
    return handleStatsRequest(request, env, url);
  }

  if (request.method === 'GET' && url.pathname === '/api/bugs') {
    return handleBugReportsList(request, env, url);
  }

  const attachmentMatch = url.pathname.match(/^\/api\/bugs\/(\d+)\/attachment$/);
  if (request.method === 'GET' && attachmentMatch) {
    return handleBugReportAttachment(request, env, Number(attachmentMatch[1]));
  }

  return new Response('Not found', { status: 404 });
}

async function handleTelemetryIngest(request, env) {
  let payload;
  try {
    payload = await request.json();
  } catch {
    return new Response('Invalid JSON', { status: 400 });
  }

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

  const results = await env.TELEMETRY_DB.batch(statements);

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

  if (actionStatements.length > 0) {
    await env.TELEMETRY_DB.batch(actionStatements);
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
  let payload;
  try {
    payload = await request.json();
  } catch {
    return new Response('Invalid JSON', { status: 400 });
  }

  const report = validateBugReport(payload);
  if (report === null) {
    return new Response('Bug report failed validation', { status: 400 });
  }

  let attachmentKey = null;
  if (report.attachment) {
    attachmentKey = `${report.reportId}/${report.attachment.fileName}`;
    const bytes = Uint8Array.from(atob(report.attachment.contentBase64), (c) => c.charCodeAt(0));
    await env.BUG_REPORT_ATTACHMENTS.put(attachmentKey, bytes, {
      httpMetadata: { contentType: report.attachment.contentType },
    });
  }

  await env.TELEMETRY_DB
    .prepare(
      `INSERT INTO bug_reports
         (report_id, category, summary, description, app_version, profile,
          technical_summary, attachment_key, environment, received_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    )
    .bind(
      report.reportId,
      report.category,
      report.summary,
      report.description,
      report.appVersion,
      report.profile,
      report.technicalSummary,
      attachmentKey,
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

async function handleBugReportAttachment(request, env, bugReportRowId) {
  const auth = await createPasswordAuthProvider(env).requireSession(request);
  if (!auth.authorized) {
    return auth.response;
  }

  const row = await env.TELEMETRY_DB
    .prepare('SELECT attachment_key FROM bug_reports WHERE id = ?')
    .bind(bugReportRowId)
    .first();
  if (!row?.attachment_key) {
    return new Response('Not found', { status: 404 });
  }

  const object = await env.BUG_REPORT_ATTACHMENTS.get(row.attachment_key);
  if (!object) {
    return new Response('Not found', { status: 404 });
  }

  return new Response(object.body, {
    status: 200,
    headers: { 'Content-Type': object.httpMetadata?.contentType ?? 'image/png' },
  });
}
