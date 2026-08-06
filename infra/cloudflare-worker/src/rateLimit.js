// Per-IP rate limiting for the unauthenticated lookup routes, backed by the
// Cloudflare `[[ratelimits]]` binding declared in wrangler.toml.
//
// The binding is deliberately optional at runtime: `node --test` and a plain
// `wrangler dev` session run without it, and a Worker that 500s or 429s every
// request because a binding is missing would be worse than one that answers.
// These routes are read-only advisory probes, never a credential check, so
// failing open is the right trade -- see requestSecurity.js for the limits
// that are *not* allowed to fail open.

/**
 * Stable per-caller bucket key. Cloudflare always sets CF-Connecting-IP on
 * the edge; the fallback only applies to local runs, where every caller
 * shares one bucket.
 *
 * @param {Request} request
 * @returns {string}
 */
export function rateLimitKey(request) {
  return request.headers.get('CF-Connecting-IP') || 'local';
}

/**
 * True when the caller is still within budget (or no limiter is bound).
 *
 * @param {{ limit: (options: { key: string }) => Promise<{ success: boolean }> } | undefined} limiter
 * @param {string} key
 * @returns {Promise<boolean>}
 */
export async function withinRateLimit(limiter, key) {
  if (!limiter || typeof limiter.limit !== 'function') {
    return true;
  }

  try {
    const outcome = await limiter.limit({ key });
    return outcome?.success !== false;
  } catch {
    return true;
  }
}
