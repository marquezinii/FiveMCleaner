import { readBoundedJson } from '../requestSecurity.js';
import { RESOURCE_ID } from './mercadoPagoApi.js';
import { handleMercadoPagoWebhook } from './mercadoPagoWebhook.js';
import { handleMercadoPagoPaymentWebhook } from './mercadoPagoPayments.js';
import { verifyMercadoPagoSignature } from './mercadoPagoSignature.js';

const TOPICS = ['subscription_preapproval', 'subscription_authorized_payment', 'payment'];
const error = (code, status) => Response.json({ error: code }, { status });

export async function handleMercadoPagoNotification(request, env, options = {}) {
  const url = new URL(request.url);
  const types = url.searchParams.getAll('type');
  if (types.length > 1 || (types.length === 1 && !TOPICS.includes(types[0]))) return error('invalid-webhook', 400);
  let type = types[0];
  if (!type && request.body) {
    const ids = url.searchParams.getAll('data.id');
    const requestId = request.headers.get('x-request-id');
    const signature = request.headers.get('x-signature');
    if (ids.length !== 1 || !RESOURCE_ID.test(ids[0]) || !RESOURCE_ID.test(requestId ?? '')
      || typeof signature !== 'string' || signature.length > 256) return error('invalid-webhook', 400);
    if (!await verifyMercadoPagoSignature({ requestUrl: request.url, requestId,
      signatureHeader: signature, secret: env.MERCADO_PAGO_WEBHOOK_SECRET })) return error('invalid-signature', 401);
    const body = await readBoundedJson(request, 8192);
    if (!body || typeof body !== 'object') return error('invalid-webhook', 400);
    // Subscription documentation describes body.type without guaranteeing its
    // query counterpart. This is a routing hint only, never payment authority.
    if (body.type !== undefined) {
      if (!TOPICS.includes(body.type) || String(body.data?.id) !== ids[0]) return error('invalid-webhook', 400);
      type = body.type;
    }
  }
  return type === 'payment' || type === 'subscription_authorized_payment'
    ? handleMercadoPagoPaymentWebhook(request, env, { ...options, notificationType: type })
    : handleMercadoPagoWebhook(request, env, options);
}
