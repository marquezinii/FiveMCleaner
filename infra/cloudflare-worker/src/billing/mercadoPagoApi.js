import { readBoundedJson } from '../requestSecurity.js';

export const OFFER_KEY = 'ralven_pro_monthly';
export const RESOURCE_ID = /^[A-Za-z0-9_-]{1,128}$/;

export class BillingError extends Error {
  constructor(code, status = 503) { super(code); this.code = code; this.status = status; }
}

export function billingConfig(env) {
  const amount = env.MERCADO_PAGO_AMOUNT_CENTS ?? '1990';
  const amountCents = /^\d{1,7}$/.test(String(amount)) ? Number(amount) : 0;
  let backUrl = null;
  try {
    const url = new URL(env.MERCADO_PAGO_RETURN_URL);
    if (url.protocol === 'https:' && !url.username && !url.password && !url.hash) backUrl = url.href;
  } catch { /* An absent or invalid configured return URL disables checkout. */ }
  const providerReady = typeof env.MERCADO_PAGO_ACCESS_TOKEN === 'string' && env.MERCADO_PAGO_ACCESS_TOKEN.length > 0
    && typeof env.MERCADO_PAGO_WEBHOOK_SECRET === 'string' && env.MERCADO_PAGO_WEBHOOK_SECRET.length > 0;
  return {
    offer: { key: `${OFFER_KEY}_${amountCents > 0 && amountCents <= 100000 ? amountCents : 1990}`,
      amountCents: amountCents > 0 && amountCents <= 100000 ? amountCents : 1990, currency: 'BRL', intervalMonths: 1 },
    enabled: env.MERCADO_PAGO_BILLING_ENABLED === 'true' && providerReady && backUrl !== null && amountCents > 0 && amountCents <= 100000,
    providerReady,
    backUrl,
  };
}

export function checkoutUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === 'https:' && url.hostname === 'www.mercadopago.com.br'
      && !url.port && !url.username && !url.password && !url.hash
      && url.pathname === '/subscriptions/checkout' && url.searchParams.getAll('preapproval_id').length === 1
      && RESOURCE_ID.test(url.searchParams.get('preapproval_id') ?? '') && url.href.length <= 2048
      ? url.href : null;
  } catch { return null; }
}

export function cents(value, allowZero = false) {
  if (typeof value !== 'number' && (typeof value !== 'string' || !/^\d+(?:\.\d{1,2})?$/.test(value))) return null;
  const amount = Number(value);
  const result = Math.round(amount * 100);
  return Number.isSafeInteger(result) && (allowZero ? result >= 0 : result > 0)
    && Math.abs(amount - result / 100) < 1e-9 ? result : null;
}

export function iso(value) {
  if (typeof value !== 'string' || value.length > 64) return null;
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? new Date(timestamp).toISOString() : null;
}

export async function providerApi(env, path, { method = 'GET', body, idempotencyKey, fetchImpl = globalThis.fetch } = {}) {
  if (!billingConfig(env).providerReady) throw new BillingError('billing-unavailable');
  let response;
  try {
    response = await fetchImpl(`https://api.mercadopago.com${path}`, {
      method, redirect: 'error', signal: AbortSignal.timeout(10_000),
      headers: { Accept: 'application/json', Authorization: `Bearer ${env.MERCADO_PAGO_ACCESS_TOKEN}`,
        ...(body ? { 'Content-Type': 'application/json' } : {}),
        ...(idempotencyKey ? { 'X-Idempotency-Key': idempotencyKey } : {}) },
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
  } catch { throw new BillingError('provider-temporarily-unavailable'); }
  if (!response.ok) throw new BillingError('provider-temporarily-unavailable');
  const result = await readBoundedJson(response, 256 * 1024);
  if (result === null || typeof result !== 'object') throw new BillingError('invalid-provider-response');
  return result;
}

export async function eventKey(kind, requestId) {
  const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${kind}:${requestId}`));
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('');
}
