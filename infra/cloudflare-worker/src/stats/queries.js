// Builds parameterized SQL for every dashboard chart. Pure string/array
// construction only -- no D1 binding touched here, so the shape of each
// query can be unit tested without a database. All queries default to
// `environment = 'Production'` so a developer's own Development-tagged runs
// never skew what the dashboard shows about real users, unless a caller
// explicitly asks for a different environment.
//
// Note on "active users": telemetry events carry no device or user
// identifier by design (see docs/telemetry.md) -- there is no way to count
// unique users from this data without violating that invariant. Every
// "per day" metric here counts *events* (optimization runs), not unique
// people; the dashboard must label it that way instead of implying a user
// count it cannot actually produce.

const DEFAULT_TOP_N = 10;

function buildFilters({ from, to, appVersion, environment = 'Production' } = {}) {
  const clauses = ['environment = ?'];
  const params = [environment];

  if (from) {
    clauses.push('received_at >= ?');
    params.push(from);
  }

  if (to) {
    clauses.push('received_at <= ?');
    params.push(to);
  }

  if (appVersion) {
    clauses.push('app_version = ?');
    params.push(appVersion);
  }

  return { whereSql: clauses.join(' AND '), params };
}

/** Optimization runs (any outcome) per calendar day, oldest first. */
export function optimizationRunsPerDay(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT substr(received_at, 1, 10) AS day, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql}
          GROUP BY day
          ORDER BY day ASC`,
    params,
  };
}

/** Distribution of Windows versions among reported events. */
export function osVersionBreakdown(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT os_version, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql} AND os_version IS NOT NULL
          GROUP BY os_version
          ORDER BY runs DESC`,
    params,
  };
}

/** Distribution of FiveMCleaner app versions among reported events. */
export function appVersionBreakdown(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT app_version, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql}
          GROUP BY app_version
          ORDER BY runs DESC`,
    params,
  };
}

/** Most-applied action IDs ("most used functions"), top N by count. */
export function topActions(filters, topN = DEFAULT_TOP_N) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT a.action_id, COUNT(*) AS uses
          FROM telemetry_event_actions a
          JOIN telemetry_events e ON e.id = a.telemetry_event_id
          WHERE ${whereSql}
          GROUP BY a.action_id
          ORDER BY uses DESC
          LIMIT ?`,
    params: [...params, topN],
  };
}

/** Average and count of completed-optimization execution time, in ms. */
export function averageOptimizationTimeMs(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT AVG(execution_time_ms) AS average_ms, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql} AND event_name = 'optimization-completed'`,
    params,
  };
}

/** Success rate: completed vs. every outcome (completed/failed/cancelled). */
export function successRate(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT
            SUM(CASE WHEN event_name = 'optimization-completed' THEN 1 ELSE 0 END) AS completed,
            COUNT(*) AS total
          FROM telemetry_events
          WHERE ${whereSql}`,
    params,
  };
}

/** Failed-run error categories, broken down by app version. */
export function errorsByVersion(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT app_version, error_category, COUNT(*) AS occurrences
          FROM telemetry_events
          WHERE ${whereSql} AND event_name = 'optimization-failed' AND error_category IS NOT NULL
          GROUP BY app_version, error_category
          ORDER BY app_version DESC, occurrences DESC`,
    params,
  };
}

/** Most common CPU models, top N by count. */
export function topCpuModels(filters, topN = DEFAULT_TOP_N) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT cpu_model, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql} AND cpu_model IS NOT NULL
          GROUP BY cpu_model
          ORDER BY runs DESC
          LIMIT ?`,
    params: [...params, topN],
  };
}

/** Most common GPU models, top N by count. */
export function topGpuModels(filters, topN = DEFAULT_TOP_N) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT gpu_model, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql} AND gpu_model IS NOT NULL
          GROUP BY gpu_model
          ORDER BY runs DESC
          LIMIT ?`,
    params: [...params, topN],
  };
}

/** Distribution of RAM buckets (GiB) among reported events. */
export function ramBucketBreakdown(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT ram_bucket_gib, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql} AND ram_bucket_gib IS NOT NULL
          GROUP BY ram_bucket_gib
          ORDER BY ram_bucket_gib ASC`,
    params,
  };
}

/** Distribution of chosen optimization profiles (Light/Balanced/Aggressive). */
export function profileBreakdown(filters) {
  const { whereSql, params } = buildFilters(filters);
  return {
    sql: `SELECT profile, COUNT(*) AS runs
          FROM telemetry_events
          WHERE ${whereSql} AND profile IS NOT NULL
          GROUP BY profile
          ORDER BY runs DESC`,
    params,
  };
}
