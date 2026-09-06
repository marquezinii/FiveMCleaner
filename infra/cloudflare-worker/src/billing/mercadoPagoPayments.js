import { BillingError, cents, eventKey, iso, providerApi, RESOURCE_ID } from './mercadoPagoApi.js';
import { parsePreapproval, reconcilePreapproval } from './mercadoPagoWebhook.js';
import { verifyMercadoPagoSignature } from './mercadoPagoSignature.js';

export function monthlyPeriodEnd(start) {
  const date = new Date(start);
  const day = date.getUTCDate();
  date.setUTCDate(1);
  date.setUTCMonth(date.getUTCMonth() + 1);
  const lastDay = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate();
  date.setUTCDate(Math.min(day, lastDay));
  return date.toISOString();
}

export function refreshEntitlementStatement(db, uid, now) {
  return db.prepare(`INSERT INTO account_entitlements
    (account_uid, entitlement_key, state, subscription_id, valid_from, valid_until,
      provider_updated_at, last_event_id, updated_at)
    SELECT s.account_uid, 'ralven_pro', 'active', s.id, p.period_start, p.period_end,
      p.provider_updated_at, p.last_event_id, ?
    FROM billing_payments p JOIN billing_subscriptions s ON s.id = p.subscription_id
    WHERE s.account_uid = ? AND p.state = 'approved' AND p.refunded_cents = 0
      AND p.period_start <= ? AND p.period_end > ?
    ORDER BY p.period_end DESC, p.provider_updated_at DESC LIMIT 1
    ON CONFLICT(account_uid, entitlement_key) DO UPDATE SET
      state = excluded.state, subscription_id = excluded.subscription_id,
      valid_from = excluded.valid_from, valid_until = excluded.valid_until,
      provider_updated_at = excluded.provider_updated_at, last_event_id = excluded.last_event_id,
      updated_at = excluded.updated_at`).bind(now, uid, now, now);
}

export function revokeEntitlementStatement(db, uid, now) {
  return db.prepare(`UPDATE account_entitlements SET
    state = CASE WHEN valid_until <= ? THEN 'expired' ELSE 'revoked' END, updated_at = ?,
    last_event_id = COALESCE((SELECT p.last_event_id FROM billing_payments p
      JOIN billing_subscriptions s ON s.id = p.subscription_id WHERE s.account_uid = account_entitlements.account_uid
      ORDER BY p.provider_updated_at DESC LIMIT 1), last_event_id)
    WHERE account_uid = ? AND entitlement_key = 'ralven_pro'
    AND NOT EXISTS (SELECT 1 FROM billing_payments p JOIN billing_subscriptions s ON s.id = p.subscription_id
      WHERE s.account_uid = ? AND p.state = 'approved' AND p.refunded_cents = 0
        AND p.period_start <= ? AND p.period_end > ?)`)
    .bind(now, now, uid, uid, now, now);
}

export async function reconcileAuthorizedPayment(env, invoiceId, requestId, options = {}) {
  if (!RESOURCE_ID.test(String(invoiceId))) throw new BillingError('invalid-webhook', 400);
  const invoice = await providerApi(env, `/authorized_payments/${encodeURIComponent(invoiceId)}`, options);
  let paymentId = String(invoice.payment?.id ?? '');
  let historicalPayment = null;
  if (options.expectedPaymentId && options.expectedPaymentId !== paymentId) {
    historicalPayment = await env.TELEMETRY_DB.prepare(`SELECT * FROM billing_payments
      WHERE provider_payment_id = ? AND authorized_payment_id = ?`).bind(options.expectedPaymentId, String(invoiceId)).first();
    if (!historicalPayment) throw new BillingError('billing-payment-mismatch', 409);
    paymentId = options.expectedPaymentId;
  }
  const subscriptionId = invoice.preapproval_id;
  if (String(invoice.id) !== String(invoiceId) || !RESOURCE_ID.test(subscriptionId ?? '')
    || !RESOURCE_ID.test(paymentId)) throw new BillingError('invalid-provider-response');
  const payment = await providerApi(env, `/v1/payments/${encodeURIComponent(paymentId)}`, options);
  const subscription = await providerApi(env, `/preapproval/${encodeURIComponent(subscriptionId)}`, options);
  const preapproval = parsePreapproval(subscription, subscriptionId);
  if (!preapproval || subscription.auto_recurring.frequency !== 1 || subscription.auto_recurring.frequency_type !== 'months') {
    throw new BillingError('invalid-provider-response');
  }
  const db = env.TELEMETRY_DB;
  const intent = await db.prepare(`SELECT * FROM billing_checkout_intents
    WHERE provider = 'mercado_pago' AND external_reference = ? LIMIT 1`).bind(preapproval.externalReference).first();
  if (!intent) return; // A valid provider event can belong to another product.
  if (intent.amount_cents !== preapproval.amountCents || intent.currency !== preapproval.currency
    || (intent.provider_checkout_id && intent.provider_checkout_id !== subscriptionId)) throw new BillingError('billing-payment-mismatch', 409);
  const periodStart = historicalPayment?.period_start ?? iso(invoice.debit_date);
  const updatedAt = iso(payment.date_last_updated);
  const amount = cents(payment.transaction_amount);
  const refunded = cents(payment.transaction_amount_refunded ?? 0, true);
  const paymentStatus = payment.status === 'canceled' ? 'cancelled' : payment.status;
  const status = ['in_process', 'in_mediation', 'authorized'].includes(paymentStatus) ? 'pending' : paymentStatus;
  if (status === 'approved' && payment.transaction_amount_refunded == null) {
    throw new BillingError('invalid-provider-response');
  }
  if (String(payment.id) !== paymentId || periodStart === null || updatedAt === null
    || amount !== intent.amount_cents || cents(invoice.transaction_amount) !== amount
    || payment.currency_id !== intent.currency || invoice.currency_id !== intent.currency
    || refunded === null || refunded > amount
    || !['approved', 'pending', 'rejected', 'refunded', 'cancelled', 'charged_back'].includes(status)) {
    throw new BillingError('billing-payment-mismatch', 409);
  }
  // An invoice only links this exact payment to the mandate. Its processed state
  // is never proof of success: the canonical payment status controls access.
  if (status === 'approved' && iso(payment.date_approved) === null) throw new BillingError('invalid-provider-response');
  const preapprovalResponse = await reconcilePreapproval(db, preapproval,
    await eventKey('invoice-preapproval', `${subscriptionId}:${preapproval.providerUpdatedAt}`), subscriptionId);
  if (!preapprovalResponse.ok) throw new BillingError('billing-temporarily-unavailable');
  const owner = await db.prepare(`SELECT id FROM billing_subscriptions WHERE provider = 'mercado_pago'
    AND provider_subscription_id = ? AND account_uid = ? AND checkout_intent_id = ?`)
    .bind(subscriptionId, intent.account_uid, intent.id).first();
  if (!owner) throw new BillingError('billing-payment-mismatch', 409);
  const now = new Date().toISOString();
  const key = await eventKey('authorized-payment', requestId);
  const existing = await db.prepare(`SELECT resource_id FROM billing_webhook_events
    WHERE provider = 'mercado_pago' AND provider_request_id = ?`).bind(key).first();
  if (existing && existing.resource_id !== String(invoiceId)) throw new BillingError('request-id-conflict', 409);
  await db.batch([
    db.prepare(`INSERT INTO billing_webhook_events
      (provider, provider_request_id, resource_id, received_at, processing_outcome, processed_at)
      VALUES ('mercado_pago', ?, ?, ?, 'pending', NULL) ON CONFLICT DO NOTHING`).bind(key, String(invoiceId), now),
    db.prepare(`INSERT INTO billing_payments
      (provider_payment_id, authorized_payment_id, subscription_id, state, amount_cents, refunded_cents,
       currency, period_start, period_end, provider_updated_at, last_event_id, updated_at)
      SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, e.id, ? FROM billing_webhook_events e
      WHERE e.provider = 'mercado_pago' AND e.provider_request_id = ? AND e.resource_id = ?
      ON CONFLICT(provider_payment_id) DO UPDATE SET state = excluded.state, refunded_cents = excluded.refunded_cents,
        provider_updated_at = excluded.provider_updated_at, last_event_id = excluded.last_event_id, updated_at = excluded.updated_at
      WHERE billing_payments.subscription_id = excluded.subscription_id
        AND billing_payments.authorized_payment_id = excluded.authorized_payment_id
        AND billing_payments.period_start = excluded.period_start AND billing_payments.period_end = excluded.period_end
        AND billing_payments.amount_cents = excluded.amount_cents AND billing_payments.currency = excluded.currency
        AND billing_payments.provider_updated_at < excluded.provider_updated_at`)
      .bind(paymentId, String(invoiceId), owner.id, status, amount, refunded, intent.currency,
        periodStart, monthlyPeriodEnd(periodStart), updatedAt, now, key, String(invoiceId)),
    refreshEntitlementStatement(db, intent.account_uid, now),
    revokeEntitlementStatement(db, intent.account_uid, now),
    db.prepare(`UPDATE billing_webhook_events SET processing_outcome = 'processed', processed_at = ?
      WHERE provider = 'mercado_pago' AND provider_request_id = ? AND resource_id = ?
      AND EXISTS(SELECT 1 FROM billing_payments WHERE provider_payment_id = ? AND authorized_payment_id = ? AND subscription_id = ?)`)
      .bind(now, key, String(invoiceId), paymentId, String(invoiceId), owner.id),
  ]);
  const event = await db.prepare(`SELECT processing_outcome FROM billing_webhook_events
    WHERE provider = 'mercado_pago' AND provider_request_id = ?`).bind(key).first();
  if (event?.processing_outcome !== 'processed') throw new BillingError('billing-payment-mismatch', 409);
}

export async function handleMercadoPagoPaymentWebhook(request, env, options = {}) {
  const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
  const url = new URL(request.url);
  const ids = url.searchParams.getAll('data.id');
  const types = url.searchParams.getAll('type');
  if (types.length === 0 && options.notificationType) types.push(options.notificationType);
  const requestId = request.headers.get('x-request-id');
  const signature = request.headers.get('x-signature');
  if (ids.length !== 1 || types.length !== 1 || !RESOURCE_ID.test(ids[0]) || !RESOURCE_ID.test(requestId ?? '')
    || typeof signature !== 'string' || signature.length > 256
    || !['subscription_authorized_payment', 'payment'].includes(types[0])) return json({ error: 'invalid-webhook' }, 400);
  if (!await verifyMercadoPagoSignature({ requestUrl: request.url, requestId,
    signatureHeader: signature, secret: env.MERCADO_PAGO_WEBHOOK_SECRET })) {
    return json({ error: 'invalid-signature' }, 401);
  }
  try {
    let invoiceId = ids[0];
    if (types[0] === 'payment') {
      const row = await env.TELEMETRY_DB.prepare(`SELECT authorized_payment_id FROM billing_payments WHERE provider_payment_id = ?`).bind(ids[0]).first();
      if (row) invoiceId = row.authorized_payment_id;
      else {
        const result = await providerApi(env, `/authorized_payments/search?payment_id=${encodeURIComponent(ids[0])}&limit=10`, options);
        if (!Array.isArray(result.results)) throw new BillingError('invalid-provider-response');
        const matches = result.results.filter(item => String(item.payment?.id) === ids[0]);
        if (matches.length !== 1 || result.results.length >= 10) throw new BillingError('billing-payment-awaiting-invoice');
        invoiceId = String(matches[0].id);
      }
    }
    await reconcileAuthorizedPayment(env, invoiceId, requestId,
      { ...options, ...(types[0] === 'payment' ? { expectedPaymentId: ids[0] } : {}) });
    return json({ accepted: true });
  } catch (error) {
    return json({ error: error instanceof BillingError ? error.code : 'billing-temporarily-unavailable' },
      error instanceof BillingError ? error.status : 503);
  }
}
