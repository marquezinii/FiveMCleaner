// Pure URL-building and response-shaping helpers for talking to the
// telemetry Worker's authenticated /api/stats/* endpoints. Kept separate
// from the DOM-touching glue in app.js so these are unit testable without a
// browser or a live Worker.

/**
 * Resolves which Worker base URL the dashboard should talk to. The `?api=`
 * override exists only for local development (dashboard on one localhost
 * port, `wrangler dev` on another). It is deliberately ignored on any other
 * host: in production an attacker could craft `?api=https://evil.example`
 * and the login form would POST the admin password there.
 */
export function resolveApiBase(defaultBase, locationHostname, searchParams) {
  const override = searchParams.get('api');
  const isLocalDev = locationHostname === 'localhost'
    || locationHostname === '127.0.0.1'
    || locationHostname === '[::1]';
  return isLocalDev && override ? override : defaultBase;
}


/** Builds the JSON stats URL for one chart, with optional filters. */
export function buildStatsUrl(baseUrl, statName, filters = {}) {
  const url = new URL(`/api/stats/${statName}`, baseUrl);
  applyFilters(url, filters);
  return url.toString();
}

/** Builds the CSV-export URL for the same chart and filters. */
export function buildCsvUrl(baseUrl, statName, filters = {}) {
  const url = new URL(`/api/stats/${statName}.csv`, baseUrl);
  applyFilters(url, filters);
  return url.toString();
}

/** Builds the URL for listing recent bug reports, with optional filters. */
export function buildBugsUrl(baseUrl, filters = {}) {
  const url = new URL('/api/bugs', baseUrl);
  applyFilters(url, filters);
  if (filters.category) {
    url.searchParams.set('category', filters.category);
  }

  return url.toString();
}

export function buildUpdaterEventsUrl(baseUrl, filters = {}) {
  const url = new URL('/api/updater-events', baseUrl);
  applyFilters(url, filters);
  return url.toString();
}

function applyFilters(url, filters) {
  if (filters.from) {
    url.searchParams.set('from', filters.from);
  }

  if (filters.to) {
    url.searchParams.set('to', filters.to);
  }

  if (filters.version) {
    url.searchParams.set('version', filters.version);
  }

  if (filters.environment) {
    url.searchParams.set('environment', filters.environment);
  }
}

/**
 * A minimal fetch wrapper the dashboard uses for every Worker call: always
 * sends cookies (`credentials: 'include'`), and treats a 401 uniformly as
 * "not logged in" regardless of which endpoint returned it. Never throws:
 * a network failure or a non-JSON body is reported as an `{ error }` result,
 * so callers like `refreshAll` can render gracefully instead of leaving the
 * page stuck on "Atualizando dados…" (which is what a thrown fetch rejection
 * did -- `Promise.all` rejected and the refresh status was never cleared).
 */
export async function requestJson(url, options = {}, fetchImpl = fetch) {
  let response;
  try {
    response = await fetchImpl(url, { ...options, credentials: 'include' });
  } catch {
    return { error: 'network-error' };
  }

  if (response.status === 401) {
    return { unauthorized: true };
  }

  if (!response.ok) {
    return { error: `request-failed-${response.status}` };
  }

  try {
    return { data: await response.json() };
  } catch {
    return { error: 'invalid-response' };
  }
}
