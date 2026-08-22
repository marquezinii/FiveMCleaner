import { validateBatch } from './validateEvent.js';
import { validateBugReport } from './bugReports/validateSubmission.js';
import { recentBugReports } from './bugReports/queries.js';
import { validateUpdaterEvent } from './updaterEvents/validateSubmission.js';
import { recentUpdaterEvents } from './updaterEvents/queries.js';
import { createPasswordAuthProvider } from './auth/passwordAuthProvider.js';
import { requireFirebaseUser } from './auth/firebaseIdToken.js';
import {
  validateAccountProfile,
  createAccountProfile,
  deleteAccountProfile,
  fetchAccountProfile,
  normalizeUsername,
  isUsernameAvailable,
} from './auth/accountProfile.js';
import { rateLimitKey, withinRateLimit, withinRequiredRateLimit } from './rateLimit.js';
import * as queries from './stats/queries.js';
import { toCsv } from './stats/csv.js';
import { buildCorsHeaders, isAllowedDashboardOrigin, withCorsHeaders } from './cors.js';
import { readBoundedJson } from './requestSecurity.js';
import { parseReleaseManifest } from './releaseManifest.js';
import { validateLiveAlertUpdate } from './liveAlert/validateSubmission.js';
import { buildLiveAlertUpsert, toLiveAlertResponse } from './liveAlert/store.js';

const MAX_TELEMETRY_BODY_BYTES = 512 * 1024;
const MAX_BUG_REPORT_BODY_BYTES = 128 * 1024;
const MAX_UPDATER_EVENT_BODY_BYTES = 4 * 1024;
const MAX_ACCOUNT_PROFILE_BODY_BYTES = 4 * 1024;
const MAX_LIVE_ALERT_BODY_BYTES = 4 * 1024;

// FiveMCleaner anonymous telemetry + bug reports + admin dashboard API
// Worker. See wrangler.toml and README.md for deployment status of each
// route. Bug reports are text-only -- no attachment/screenshot support, no
// R2 dependency -- everything lives in D1.
//
// Routes:
//   POST    /telemetry             -- ingest a batch of telemetry events (no auth; validated server-side)
//   POST    /bugs                  -- ingest one bug report, text-only (no auth; validated server-side)
//   POST    /account/profile       -- create the username/first/last-name profile for a Firebase account (requires a valid Firebase ID token)
//   GET     /account/profile       -- read the caller's own username/first/last-name profile (requires a valid Firebase ID token)
//   DELETE  /account/profile       -- delete the caller's own profile before its Firebase account is deleted
//   GET     /account/username-available -- advisory "is this username free?" probe for the registration form (no auth; rate limited per IP)
//   POST    /admin/login           -- { password } -> session cookie
//   POST    /admin/logout          -- clears the session cookie
//   GET     /api/stats/:name       -- one chart's data (requires a valid session)
//   GET     /api/stats/:name.csv   -- same data as CSV (requires a valid session)
//   GET     /api/bugs              -- recent bug reports, newest first (requires a valid session)
//   GET     /live-alert            -- current admin-broadcast alert, { id, message, active } (no auth; rate limited per IP)
//   POST    /admin/live-alert      -- { message?, active } -> upsert the single live alert row (requires a valid session)
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
  'average-time': queries.averageOptimizationTimeMs,
  'success-rate': queries.successRate,
  'errors-by-version': queries.errorsByVersion,
  'error-categories': queries.errorCategoryBreakdown,
  'recent-failures': queries.recentFailures,
  'top-cpu': queries.topCpuModels,
  'top-gpu': queries.topGpuModels,
  'ram-buckets': queries.ramBucketBreakdown,
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

// Every route below answers with a JSON body -- this is the one shared shape
// (Content-Type plus whatever status/extra headers a route needs). Cache-
// Control is deliberately not set here: withCorsHeaders already defaults it
// to 'no-store' on every response the fetch handler returns, so repeating it
// per route would just be the same value twice.
function jsonResponse(body, status = 200, extraHeaders = {}) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...extraHeaders },
  });
}

// Shared shape for the four routes backed by a `[[ratelimits]]` binding.
// Returns the 429 Response to return immediately, or null when the caller is
// within budget. `required` distinguishes the write routes (fail closed, see
// withinRequiredRateLimit) from the advisory username lookup (fail open, see
// withinRateLimit).
async function rejectIfRateLimited(limiter, request, required = true) {
  const withinLimit = required
    ? await withinRequiredRateLimit(limiter, rateLimitKey(request))
    : await withinRateLimit(limiter, rateLimitKey(request));
  return withinLimit ? null : jsonResponse({ error: 'rate-limited' }, 429);
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
  if (request.method === 'POST'
    && url.pathname.startsWith('/admin/')
    && !isAllowedDashboardOrigin(request.headers.get('Origin'), env.DASHBOARD_ORIGIN)) {
    return new Response('Forbidden', { status: 403 });
  }

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
  if (request.method === 'POST' && url.pathname === '/account/profile') {
    return handleAccountProfileCreate(request, env);
  }
  if (request.method === 'GET' && url.pathname === '/account/profile') {
    return handleAccountProfileGet(request, env);
  }
  if (request.method === 'DELETE' && url.pathname === '/account/profile') {
    return handleAccountProfileDelete(request, env);
  }
  if (request.method === 'GET' && url.pathname === '/account/username-available') {
    return handleUsernameAvailability(request, env, url);
  }
  if (request.method === 'GET' && url.pathname === '/live-alert') {
    return handleLiveAlertGet(request, env);
  }
  if (request.method === 'POST' && url.pathname === '/admin/live-alert') {
    return handleLiveAlertUpdate(request, env);
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
  if (request.method === 'GET' && url.pathname === '/api/updater-events') {
    return handleUpdaterEventsList(request, env, url);
  }

  return new Response('Not found', { status: 404 });
}

function handleSignedReleaseManifest(env) {
  const manifest = env.RELEASE_MANIFEST_JSON;
  if (typeof manifest !== 'string' || manifest.length === 0) {
    return new Response('Release manifest unavailable', { status: 503 });
  }
  if (parseReleaseManifest(manifest) === null) {
    return new Response('Release manifest invalid', { status: 500 });
  }
  // No explicit Cache-Control here -- withCorsHeaders already defaults every
  // response (including this signed manifest) to 'no-store'.
  return new Response(manifest, {
    status: 200,
    headers: { 'Content-Type': 'application/json; charset=utf-8' },
  });
}

async function handleUpdaterEventIngest(request, env) {
  const limited = await rejectIfRateLimited(env.UPDATER_EVENT_LIMITER, request);
  if (limited) return limited;

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

// Advisory "is this username free?" probe for the registration form, so the
// user is told a name is taken while typing instead of only after the
// Firebase account already exists and the profile insert fails with 409.
//
// Necessarily unauthenticated -- it runs before the account does -- so it is
// rate limited per IP and answers a bare boolean: never who holds the name,
// never anything about that account. Enumeration is still theoretically
// possible at the allowed rate; the exposure is limited to "this display
// name exists", which the app shows publicly anyway.
//
// Advisory, not authoritative: the UNIQUE index on account_profiles remains
// the only real arbiter, and handleAccountProfileCreate still returns 409.
async function handleUsernameAvailability(request, env, url) {
  const limited = await rejectIfRateLimited(env.USERNAME_LOOKUP_LIMITER, request, false);
  if (limited) return limited;

  const normalized = normalizeUsername(url.searchParams.get('u'));
  if (normalized === null) {
    return jsonResponse({ error: 'invalid-username' }, 400);
  }

  const available = await isUsernameAvailable(env.TELEMETRY_DB, normalized);
  return jsonResponse({ available });
}

// Public broadcast the desktop app polls (startup + hourly) to show an
// admin-authored banner -- see docs/superpowers/specs/2026-08-17-live-alerts-design.md.
// Necessarily unauthenticated, same trade as the username lookup above: a
// rate-limited, read-only, advisory GET.
async function handleLiveAlertGet(request, env) {
  const limited = await rejectIfRateLimited(env.LIVE_ALERT_LIMITER, request, false);
  if (limited) return limited;

  const row = await env.TELEMETRY_DB
    .prepare('SELECT message, active, updated_at FROM live_alert WHERE id = 1')
    .first();
  return jsonResponse(toLiveAlertResponse(row));
}

// Admin-only write side of the same feature. `message` is optional so the
// dashboard's "Desativar" button can turn the alert off without resending
// the stored text -- see src/liveAlert/validateSubmission.js.
async function handleLiveAlertUpdate(request, env) {
  const auth = await createPasswordAuthProvider(env).requireSession(request);
  if (!auth.authorized) return auth.response;

  const payload = await readBoundedJson(request, MAX_LIVE_ALERT_BODY_BYTES);
  if (payload === null) return new Response('Invalid JSON', { status: 400 });

  const update = validateLiveAlertUpdate(payload);
  if (update === null) return jsonResponse({ error: 'invalid-live-alert' }, 400);

  const { sql, params } = buildLiveAlertUpsert(update, new Date().toISOString());
  await env.TELEMETRY_DB.prepare(sql).bind(...params).run();
  return jsonResponse({ success: true });
}

// Completes the profile of a Firebase-authenticated account with the
// fields Firebase Authentication REST doesn't manage: a unique username,
// first name, last name. Requires a valid Firebase ID token (verified
// server-side, see auth/firebaseIdToken.js) -- the uid is always taken from
// the verified token, never from the request body.
async function handleAccountProfileCreate(request, env) {
  const auth = await requireFirebaseUser(request);
  if (!auth.authorized) return auth.response;
  if (!auth.emailVerified) return jsonResponse({ error: 'email-verification-required' }, 403);

  const payload = await readBoundedJson(request, MAX_ACCOUNT_PROFILE_BODY_BYTES);
  if (payload === null) return new Response('Invalid JSON', { status: 400 });

  const profile = validateAccountProfile(payload);
  if (profile === null) {
    return jsonResponse({ error: 'invalid-profile' }, 400);
  }

  const result = await createAccountProfile(env.TELEMETRY_DB, auth.uid, profile);
  if (!result.ok) {
    const status = result.code === 'username-taken' || result.code === 'uid-taken' ? 409 : 500;
    return jsonResponse({ error: result.code }, status);
  }

  return jsonResponse({ success: true }, 201);
}

async function handleAccountProfileGet(request, env) {
  const auth = await requireFirebaseUser(request);
  if (!auth.authorized) return auth.response;

  const profile = await fetchAccountProfile(env.TELEMETRY_DB, auth.uid);
  if (profile === null) {
    return jsonResponse({ error: 'profile-not-found' }, 404);
  }

  return jsonResponse(profile);
}

async function handleAccountProfileDelete(request, env) {
  const auth = await requireFirebaseUser(request);
  if (!auth.authorized) return auth.response;

  await deleteAccountProfile(env.TELEMETRY_DB, auth.uid);
  return new Response(null, { status: 204 });
}

async function handleUpdaterEventsList(request, env, url) {
  const auth = await createPasswordAuthProvider(env).requireSession(request);
  if (!auth.authorized) return auth.response;
  const { sql, params } = recentUpdaterEvents({
    environment: url.searchParams.get('environment') || undefined,
    version: url.searchParams.get('version') || undefined,
  }, url.searchParams.get('limit'));
  try {
    const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();
    return jsonResponse(results);
  } catch (err) {
    return jsonResponse({ error: 'Database query failed' }, 500);
  }
}

async function handleTelemetryIngest(request, env) {
  const limited = await rejectIfRateLimited(env.TELEMETRY_LIMITER, request);
  if (limited) return limited;

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
          `INSERT OR IGNORE INTO telemetry_events
             (event_name, execution_time_ms, app_version, error_category,
              os_version, system_architecture, cpu_model, gpu_model,
              ram_bucket_gib, profile, environment, received_at,
              five_m_install_detected, gta_edition, optimization_target_count,
              windows_build, disk_type, free_space_gib_bucket, run_timestamp,
              days_since_last_run_bucket, backup_created, backup_restored,
              elevation_used, process_count_at_start)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
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
          event.fiveMInstallDetected === null ? null : Number(event.fiveMInstallDetected),
          event.gtaEdition,
          event.optimizationTargetCount,
          event.windowsBuild,
          event.diskType,
          event.freeSpaceGiBBucket,
          event.runTimestamp,
          event.daysSinceLastRunBucket,
          event.backupCreated === null ? null : Number(event.backupCreated),
          event.backupRestored === null ? null : Number(event.backupRestored),
          event.elevationUsed === null ? null : Number(event.elevationUsed),
          event.processCountAtStart,
        ),
    );
  }

  const results = [];
  for (const chunk of chunkStatements(statements)) {
    try {
      results.push(...await env.TELEMETRY_DB.batch(chunk));
    } catch (err) {
      console.error('Telemetry chunk failed, continuing:', err?.message || 'unknown');
    }
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
    try {
      await env.TELEMETRY_DB.batch(chunk);
    } catch (err) {
      return jsonResponse({ error: 'Database write failed for action links' }, 500);
    }
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
  try {
    const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();
    if (asCsv) {
      return new Response(toCsv(results), {
        status: 200,
        headers: {
          'Content-Type': 'text/csv; charset=utf-8',
          'Content-Disposition': `attachment; filename="${name.replace(/[^a-zA-Z0-9_-]/g, '_')}.csv"`,
        },
      });
    }
    return jsonResponse(results);
  } catch (err) {
    return jsonResponse({ error: 'Database query failed' }, 500);
  }
}

async function handleBugReportIngest(request, env) {
  const limited = await rejectIfRateLimited(env.BUG_REPORT_LIMITER, request);
  if (limited) return limited;

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

  return jsonResponse({ success: true }, 202);
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
  try {
    const { results } = await env.TELEMETRY_DB.prepare(sql).bind(...params).all();
    return jsonResponse(results);
  } catch (err) {
    return jsonResponse({ error: 'Database query failed' }, 500);
  }
}
