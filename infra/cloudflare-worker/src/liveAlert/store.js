// Pure SQL+params builder for the single-row `live_alert` table. Mirrors
// updaterEvents/queries.js: keeps the D1 call itself in index.js while
// making the query shape unit testable without a database.
//
// The row (id = 1) is seeded by the migration/schema, so this is always an
// UPDATE, never an insert.

export function buildLiveAlertUpsert({ message, active }, updatedAt) {
  if (message === undefined) {
    return {
      sql: 'UPDATE live_alert SET active = ?, updated_at = ? WHERE id = 1',
      params: [active ? 1 : 0, updatedAt],
    };
  }

  return {
    sql: 'UPDATE live_alert SET message = ?, active = ?, updated_at = ? WHERE id = 1',
    params: [message, active ? 1 : 0, updatedAt],
  };
}

/** Shapes the raw D1 row (or its absence) into the public GET /live-alert body. */
export function toLiveAlertResponse(row) {
  if (!row) {
    return { id: null, message: '', active: false };
  }

  return { id: row.updated_at, message: row.message, active: !!row.active };
}
