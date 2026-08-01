export function recentUpdaterEvents(filters = {}, requestedLimit = 100) {
  const clauses = [];
  const params = [];
  // The dashboard's environment <select> defaults to 'All'; unlike the stats
  // queries (stats/queries.js buildFilters), the updater-event query used to
  // treat 'All' as a literal value and produced WHERE environment = 'All',
  // which matches zero rows -- making the "Bugs do updater" table always
  // empty until the user picked a specific environment. 'All' means "no
  // filter", exactly like it does for every stats chart.
  if (filters.environment && filters.environment !== 'All') { clauses.push('environment = ?'); params.push(filters.environment); }
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
