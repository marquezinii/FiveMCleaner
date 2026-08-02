// Shared filter-clause helpers for every dashboard query family
// (stats, bug reports, updater events). Only the pieces that are genuinely
// identical across all three live here:
//
//   - the environment guard ("All" means "no filter", exactly as the
//     dashboard's environment <select> sends it), and
//   - the from/to date-range bounds shared by the stats and bug-report
//     queries.
//
// Everything else -- domain-specific columns (app_version, category,
// candidate_version), per-domain defaults and limits, and the final
// WHERE/1=1 formatting -- stays in each query file, where it differs by
// design. The helpers append into caller-owned arrays so each caller can
// keep its own clause order (and therefore its own bound-parameter order),
// which existing tests pin down exactly.

export function appendEnvironmentClause(clauses, params, environment) {
  if (environment && environment !== 'All') {
    clauses.push('environment = ?');
    params.push(environment);
  }
}

export function appendDateRangeClauses(clauses, params, { from, to } = {}) {
  if (from) {
    clauses.push('received_at >= ?');
    params.push(from);
  }

  if (to) {
    // The dashboard sends a calendar date, while received_at is UTC with a
    // time component. An inclusive string comparison would exclude every
    // event later on the selected final day.
    clauses.push("received_at < date(?, '+1 day')");
    params.push(to);
  }
}
