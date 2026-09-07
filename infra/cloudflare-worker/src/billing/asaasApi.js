import { readBoundedJson } from '../requestSecurity.js';

export const BILLING_PROVIDER = 'asaas';
export const OFFER_KEY = 'ralven_pro_monthly';
export const PROVIDER_ID = /^[A-Za-z0-9_-]{1,128}$/;

export class BillingError extends Error {
  constructor(code, status = 503) { super(code); this.code = code; this.status = status; }
}

export function billingConfig(env = {}) {
  const rawAmount = String(env.ASAAS_AMOUNT_CENTS ?? '1990');
  const amountCents = /^\d{1,7}$/.test(rawAmount) ? Number(rawAmount) : 0;
  const validAmount = amountCents > 0 && amountCents <= 100000;
  const environment = env.ASAAS_ENVIRONMENT;
  const accessToken = env.ASAAS_ACCESS_TOKEN;
  const webhookToken = env.ASAAS_WEBHOOK_TOKEN;
  const expectedPrefix = environment === 'production' ? '$aact_prod_' : environment === 'sandbox' ? '$aact_hmlg_' : null;
  let returnUrl = null;
  try {
    const url = new URL(env.ASAAS_RETURN_URL);
    if (url.protocol === 'https:' && !url.username && !url.password && !url.hash) returnUrl = url.href;
  } catch { /* Invalid or missing callback configuration keeps checkout disabled. */ }
  const providerReady = expectedPrefix !== null
    && typeof accessToken === 'string' && accessToken.startsWith(expectedPrefix)
    && typeof webhookToken === 'string' && webhookToken.length >= 32 && webhookToken.length <= 255
    && !/\s/.test(webhookToken) && webhookToken !== accessToken;
  return {
    apiBase: environment === 'production' ? 'https://api.asaas.com/v3' : 'https://api-sandbox.asaas.com/v3',
    environment,
    providerReady,
    returnUrl,
    offer: { key: `${OFFER_KEY}_${validAmount ? amountCents : 1990}`,
      amountCents: validAmount ? amountCents : 1990, currency: 'BRL', intervalMonths: 1 },
    enabled: env.ASAAS_BILLING_ENABLED === 'true' && providerReady && returnUrl !== null && validAmount,
  };
}

export function checkoutUrl(id) {
  return PROVIDER_ID.test(id ?? '') ? `https://asaas.com/checkoutSession/show?id=${encodeURIComponent(id)}` : null;
}

export function validateCheckoutUrl(value) {
  try {
    const url = new URL(value);
    const ids = url.searchParams.getAll('id');
    return url.protocol === 'https:' && url.hostname === 'asaas.com' && !url.port
      && !url.username && !url.password && !url.hash && url.pathname === '/checkoutSession/show'
      && [...url.searchParams.keys()].length === 1 && ids.length === 1 && PROVIDER_ID.test(ids[0])
      && url.href.length <= 2048 ? url.href : null;
  } catch { return null; }
}

export function cents(value, allowZero = false) {
  if (typeof value !== 'number' && (typeof value !== 'string' || !/^\d+(?:\.\d{1,2})?$/.test(value))) return null;
  const amount = Number(value);
  const result = Math.round(amount * 100);
  return Number.isSafeInteger(result) && (allowZero ? result >= 0 : result > 0)
    && Math.abs(amount - result / 100) < 1e-9 ? result : null;
}

export function providerDate(value) {
  if (typeof value !== 'string' || value.length > 64) return null;
  const normalized = /^\d{4}-\d{2}-\d{2}$/.test(value) ? `${value}T00:00:00.000Z`
    : /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/.test(value) ? `${value.replace(' ', 'T')}.000Z` : value;
  const timestamp = Date.parse(normalized);
  return Number.isFinite(timestamp) ? new Date(timestamp).toISOString() : null;
}

export function monthlyPeriodEnd(startIso) {
  const start = new Date(startIso);
  if (!Number.isFinite(start.valueOf())) return null;
  const day = start.getUTCDate();
  const end = new Date(start);
  end.setUTCDate(1);
  end.setUTCMonth(end.getUTCMonth() + 1);
  end.setUTCDate(Math.min(day, new Date(Date.UTC(end.getUTCFullYear(), end.getUTCMonth() + 1, 0)).getUTCDate()));
  return end.toISOString();
}

export async function eventKey(kind, value) {
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${kind}:${value}`));
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('');
}

export async function asaasApi(env, path, { method = 'GET', body, allowEmpty = false,
  fetchImpl = globalThis.fetch } = {}) {
  const config = billingConfig(env);
  if (!config.providerReady || !path.startsWith('/')) throw new BillingError('billing-unavailable');
  let response;
  try {
    response = await fetchImpl(`${config.apiBase}${path}`, {
      method, redirect: 'error', signal: AbortSignal.timeout(10_000),
      headers: { Accept: 'application/json', access_token: env.ASAAS_ACCESS_TOKEN,
        'User-Agent': 'Ralven/1.0 billing', 'Content-Type': 'application/json' },
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
  } catch { throw new BillingError('provider-temporarily-unavailable'); }
  if (!response.ok) throw new BillingError('provider-temporarily-unavailable');
  const result = await readBoundedJson(response, 256 * 1024);
  if (result === null) {
    if (allowEmpty) return {};
    throw new BillingError('invalid-provider-response');
  }
  if (typeof result !== 'object') throw new BillingError('invalid-provider-response');
  return result;
}
