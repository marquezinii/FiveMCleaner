// Pure CORS header logic, separated from index.js so it is unit testable
// without a Request/Response. The dashboard (Cloudflare Pages) and this
// Worker are meant to sit on different origins (a Pages domain and a
// Workers domain, or two different localhost ports during local testing),
// so every response needs explicit CORS headers -- browsers do not grant
// cross-origin fetch with credentials by SameSite cookie policy alone.
//
// Only ever allows origins configured in the `DASHBOARD_ORIGIN` environment
// variable (a comma-separated, non-secret Worker var, see wrangler.toml) --
// never reflects an arbitrary request Origin back, which would defeat the
// purpose of allow-listing when credentials are involved.

/** Builds the CORS response headers for `origin`, or `{}` if it does not match. */
export function isAllowedDashboardOrigin(origin, allowedOrigin) {
  return Boolean(origin && allowedOrigin?.split(',').some((candidate) => candidate.trim() === origin));
}

export function buildCorsHeaders(origin, allowedOrigin) {
  if (!isAllowedDashboardOrigin(origin, allowedOrigin)) {
    return {};
  }

  return {
    'Access-Control-Allow-Origin': origin,
    'Access-Control-Allow-Credentials': 'true',
    'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
    'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Ralven-Csrf-Token',
    Vary: 'Origin',
  };
}

/** Merges CORS headers onto an existing Response without altering its body or status. */
export function withCorsHeaders(response, corsHeaders) {
  const headers = new Headers(response.headers);
  for (const [name, value] of Object.entries(corsHeaders)) {
    headers.set(name, value);
  }
  if (!headers.has('Cache-Control')) headers.set('Cache-Control', 'no-store');
  headers.set('Referrer-Policy', 'no-referrer');
  headers.set('X-Content-Type-Options', 'nosniff');

  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers,
  });
}
