// Pure URL-building and response-shaping helpers for talking to the
// telemetry Worker's authenticated /api/stats/* endpoints. Kept separate
// from the DOM-touching glue in app.js so these are unit testable without a
// browser or a live Worker.

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
 * "not logged in" regardless of which endpoint returned it.
 */
export async function requestJson(url, options = {}, fetchImpl = fetch) {
  const response = await fetchImpl(url, { ...options, credentials: 'include' });
  if (response.status === 401) {
    return { unauthorized: true };
  }

  if (!response.ok) {
    return { error: `request-failed-${response.status}` };
  }

  return { data: await response.json() };
}
