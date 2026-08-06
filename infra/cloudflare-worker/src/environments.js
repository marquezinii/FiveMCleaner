// The only two environments that exist anywhere in the telemetry pipeline
// (client, Worker, dashboard). Shared by every validator so the closed set
// can never drift between telemetry events, bug reports and updater events.
// There is deliberately no 'All' entry here: 'All' is a dashboard-only
// filter escape hatch meaning "no environment filter", never a stored value.

export const ALLOWED_ENVIRONMENTS = new Set(['Development', 'Production']);
