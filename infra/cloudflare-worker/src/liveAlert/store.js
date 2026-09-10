import { LIVE_ALERT_SEVERITIES } from './validateSubmission.js';

// Pure SQL+params builder for the single-row `live_alert` table. Mirrors
// updaterEvents/queries.js: keeps the D1 call itself in index.js while
// making the query shape unit testable without a database.
//
// The row (id = 1) is seeded by the migration/schema, so this is always an
// UPDATE, never an insert.

export function buildLiveAlertUpsert({ message, active, severity }, updatedAt) {
  if (message === undefined) {
    return {
      sql: severity === undefined
        ? 'UPDATE live_alert SET active = ?, updated_at = ? WHERE id = 1'
        : 'UPDATE live_alert SET active = ?, severity = ?, updated_at = ? WHERE id = 1',
      params: severity === undefined
        ? [active ? 1 : 0, updatedAt]
        : [active ? 1 : 0, severity, updatedAt],
    };
  }

  return {
    sql: severity === undefined
      ? 'UPDATE live_alert SET message = ?, active = ?, updated_at = ? WHERE id = 1'
      : 'UPDATE live_alert SET message = ?, active = ?, severity = ?, updated_at = ? WHERE id = 1',
    params: severity === undefined
      ? [message, active ? 1 : 0, updatedAt]
      : [message, active ? 1 : 0, severity, updatedAt],
  };
}

/** Shapes the raw D1 row (or its absence) into the public GET /live-alert body. */
export function toLiveAlertResponse(row) {
  if (!row) {
    return { id: null, message: '', active: false, severity: 'important' };
  }

  const active = !!row.active;
  return {
    id: row.updated_at,
    message: active ? row.message : '',
    active,
    severity: LIVE_ALERT_SEVERITIES.has(row.severity) ? row.severity : 'important',
  };
}
