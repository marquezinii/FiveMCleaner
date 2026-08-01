export function recentUpdaterEvents(filters = {}, requestedLimit = 100) {
  const clauses = [];
  const params = [];
  if (filters.environment) { clauses.push('environment = ?'); params.push(filters.environment); }
  if (filters.version) { clauses.push('candidate_version = ?'); params.push(filters.version); }
  const limit = Math.min(Math.max(Number(requestedLimit) || 100, 1), 500);
  params.push(limit);
  return {
    sql: `SELECT event_id, stage, outcome, error_code, previous_version, candidate_version, environment, received_at
          FROM updater_events ${clauses.length ? `WHERE ${clauses.join(' AND ')}` : ''}
          ORDER BY received_at DESC LIMIT ?`,
    params,
  };
}
