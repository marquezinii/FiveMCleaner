import { billingConfig, BillingError, checkoutUrl, eventKey, OFFER_KEY, providerApi, RESOURCE_ID } from './mercadoPagoApi.js';
import { parsePreapproval, reconcilePreapproval } from './mercadoPagoWebhook.js';
import { monthlyPeriodEnd, reconcileAuthorizedPayment } from './mercadoPagoPayments.js';

async function openIntent(db, uid) {
  return db.prepare(`SELECT * FROM billing_checkout_intents WHERE account_uid = ? AND state <> 'cancelled' LIMIT 1`).bind(uid).first();
}

export async function fetchAccountBilling(env, uid) {
  const config = billingConfig(env);
  const row = await env.TELEMETRY_DB.prepare(`
    SELECT i.state AS checkout_state, i.amount_cents, s.state, s.id,
      (SELECT MAX(p.period_end) FROM billing_payments p JOIN billing_subscriptions paid ON paid.id = p.subscription_id
       WHERE paid.account_uid = i.account_uid
       AND p.state = 'approved' AND p.refunded_cents = 0 AND p.period_start <= ? AND p.period_end > ?) AS access_until
    FROM billing_checkout_intents i LEFT JOIN billing_subscriptions s ON s.checkout_intent_id = i.id
    WHERE i.account_uid = ? ORDER BY CASE WHEN i.state <> 'cancelled' THEN 0 ELSE 1 END, i.created_at DESC LIMIT 1
  `).bind(new Date().toISOString(), new Date().toISOString(), uid).first();
  return {
    offer: row?.checkout_state !== 'cancelled' && row?.amount_cents
      ? { ...config.offer, key: `${OFFER_KEY}_${row.amount_cents}`, amountCents: row.amount_cents } : config.offer,
    checkoutAvailable: config.enabled && (row === null || ['created', 'pending'].includes(row.checkout_state)
      || (row.checkout_state === 'cancelled' && row.access_until === null)),
    billingUnavailableReason: config.enabled ? null : 'billing-not-configured',
    subscription: row === null ? null : {
      state: row.state ?? (row.checkout_state === 'cancelled' ? 'cancelled' : 'pending'),
      renewsAt: null,
      accessUntil: row.access_until ?? null,
      canCancel: config.providerReady && row.checkout_state !== 'cancelled',
    },
  };
}

export async function syncAccountBilling(env, auth, options = {}) {
  if (!billingConfig(env).providerReady) return;
  const db = env.TELEMETRY_DB;
  const intent = await db.prepare(`SELECT * FROM billing_checkout_intents WHERE account_uid = ?
    ORDER BY CASE WHEN state <> 'cancelled' THEN 0 ELSE 1 END, created_at DESC LIMIT 1`).bind(auth.uid).first();
  if (!intent || (!intent.provider_checkout_id && !intent.create_attempt_started_at)) return;
  if (!intent.provider_checkout_id && typeof auth.email !== 'string') throw new BillingError('email-verification-required', 403);
  const resource = await recoverResource(env, intent, auth.email, options);
  await reconcile(env, intent, resource);
  const now = new Date().toISOString();
  let reconciled = 0;
  for (let offset = 0, pages = 0; offset < 500 && pages < 10; pages++) {
    const result = await providerApi(env,
      `/authorized_payments/search?preapproval_id=${encodeURIComponent(resource.id)}&limit=100&offset=${offset}`, options);
    if (!Array.isArray(result.results)) throw new BillingError('invalid-provider-response');
    for (const invoice of result.results) {
      if (invoice.preapproval_id !== resource.id) throw new BillingError('billing-payment-mismatch', 409);
      const debitDate = typeof invoice.debit_date === 'string' ? Date.parse(invoice.debit_date) : NaN;
      if (!Number.isFinite(debitDate)) throw new BillingError('invalid-provider-response');
      if (monthlyPeriodEnd(new Date(debitDate).toISOString()) <= now || !invoice.payment?.id) continue;
      if (++reconciled > 4) throw new BillingError('billing-checkout-reconciliation-required', 409);
      await reconcileAuthorizedPayment(env, String(invoice.id), `refresh-${invoice.id}`, options);
    }
    const nextOffset = offset + result.results.length;
    const total = Number.isSafeInteger(result.paging?.total) ? result.paging.total : null;
    if ((total !== null && nextOffset >= total) || (total === null && result.results.length < 100)) return;
    if (nextOffset === offset) throw new BillingError('invalid-provider-response');
    offset = nextOffset;
  }
  throw new BillingError('billing-checkout-reconciliation-required', 409);
}

async function reconcile(env, intent, resource) {
  const parsed = parsePreapproval(resource, resource.id);
  if (!RESOURCE_ID.test(resource.id ?? '') || parsed === null
    || resource.auto_recurring.frequency !== 1 || resource.auto_recurring.frequency_type !== 'months'
    || parsed.externalReference !== intent.external_reference
    || parsed.amountCents !== intent.amount_cents || parsed.currency !== intent.currency
    || (intent.provider_checkout_id && intent.provider_checkout_id !== resource.id)) {
    throw new BillingError('invalid-provider-response');
  }
  const response = await reconcilePreapproval(env.TELEMETRY_DB, parsed,
    await eventKey('account-preapproval', `${resource.id}:${parsed.providerUpdatedAt}`), resource.id);
  if (!response.ok) throw new BillingError('billing-temporarily-unavailable');
  return resource;
}

async function recoverResource(env, intent, email, options) {
  if (intent.provider_checkout_id) {
    return providerApi(env, `/preapproval/${encodeURIComponent(intent.provider_checkout_id)}`, options);
  }
  // The provider does not document idempotent preapproval creation. Search and
  // match the opaque server reference; an inconclusive lookup never recreates it.
  let found = null;
  let complete = false;
  for (let offset = 0, pages = 0; offset < 500 && pages < 10; pages++) {
    const result = await providerApi(env, `/preapproval/search?payer_email=${encodeURIComponent(email)}&limit=50&offset=${offset}`, options);
    if (!Array.isArray(result.results)) throw new BillingError('invalid-provider-response');
    for (const item of result.results) {
      if (item.external_reference === intent.external_reference) {
        if (found !== null) throw new BillingError('billing-checkout-reconciliation-required', 409);
        found = item;
      }
    }
    const nextOffset = offset + result.results.length;
    const total = Number.isSafeInteger(result.paging?.total) ? result.paging.total : null;
    if ((total !== null && nextOffset >= total) || (total === null && result.results.length < 50)) {
      complete = true;
      break;
    }
    if (nextOffset === offset) throw new BillingError('invalid-provider-response');
    if (nextOffset >= 500) throw new BillingError('billing-checkout-reconciliation-required', 409);
    offset = nextOffset;
  }
  if (!complete || !found || !RESOURCE_ID.test(found.id ?? '')) throw new BillingError('billing-checkout-reconciliation-required', 409);
  return providerApi(env, `/preapproval/${encodeURIComponent(found.id)}`, options);
}

export async function createAccountCheckout(env, auth, payload, options = {}) {
  const config = billingConfig(env);
  if (!config.enabled) throw new BillingError('billing-unavailable');
  if (!auth.emailVerified || typeof auth.email !== 'string') throw new BillingError('email-verification-required', 403);
  if (!payload || Object.keys(payload).length !== 1 || typeof payload.offerKey !== 'string') throw new BillingError('invalid-offer', 400);
  const db = env.TELEMETRY_DB;
  const profile = await db.prepare('SELECT uid FROM account_profiles WHERE uid = ?').bind(auth.uid).first();
  if (!profile) throw new BillingError('profile-not-found', 404);
  let intent = await openIntent(db, auth.uid);
  const expectedOfferKey = intent ? `${OFFER_KEY}_${intent.amount_cents}` : config.offer.key;
  if (payload.offerKey !== expectedOfferKey) throw new BillingError('invalid-offer', 400);
  if (!intent) {
    const access = await fetchAccountBilling(env, auth.uid);
    if (access.subscription?.accessUntil) throw new BillingError('billing-access-active', 409);
    const id = crypto.randomUUID();
    const now = new Date().toISOString();
    await db.prepare(`INSERT INTO billing_checkout_intents
      (id, account_uid, provider, external_reference, offer_key, amount_cents, currency, state, created_at, updated_at)
      VALUES (?, ?, 'mercado_pago', ?, ?, ?, 'BRL', 'created', ?, ?) ON CONFLICT DO NOTHING`)
      .bind(id, auth.uid, crypto.randomUUID(), OFFER_KEY, config.offer.amountCents, now, now).run();
    intent = await openIntent(db, auth.uid);
  }
  if (!intent) throw new BillingError('billing-temporarily-unavailable');
  if (payload.offerKey !== `${OFFER_KEY}_${intent.amount_cents}`) throw new BillingError('invalid-offer', 400);
  let resource;
  if (intent.create_attempt_started_at === null && intent.provider_checkout_id === null) {
    const claimed = await db.prepare(`UPDATE billing_checkout_intents SET create_attempt_started_at = ?
      WHERE id = ? AND state = 'created' AND create_attempt_started_at IS NULL AND provider_checkout_id IS NULL`)
      .bind(new Date().toISOString(), intent.id).run();
    if (claimed.meta?.changes !== 1) throw new BillingError('billing-checkout-in-progress', 409);
    resource = await providerApi(env, '/preapproval', { ...options, method: 'POST', idempotencyKey: intent.id, body: {
      reason: 'Ralven Pro', external_reference: intent.external_reference, payer_email: auth.email,
      auto_recurring: { frequency: 1, frequency_type: 'months', transaction_amount: intent.amount_cents / 100, currency_id: 'BRL' },
      back_url: config.backUrl, status: 'pending',
    } });
  } else {
    resource = await recoverResource(env, intent, auth.email, options);
  }
  await reconcile(env, intent, resource);
  if (resource.status !== 'pending') throw new BillingError('billing-subscription-exists', 409);
  const url = checkoutUrl(resource.init_point);
  if (!url || new URL(url).searchParams.get('preapproval_id') !== resource.id) throw new BillingError('invalid-checkout-url');
  return { checkoutUrl: url };
}

export async function cancelAccountBilling(env, auth, options = {}) {
  const intent = await openIntent(env.TELEMETRY_DB, auth.uid);
  if (!intent) return fetchAccountBilling(env, auth.uid);
  if (intent.provider_checkout_id === null && intent.create_attempt_started_at === null) {
    const cancelled = await env.TELEMETRY_DB.prepare(`UPDATE billing_checkout_intents SET state = 'cancelled', updated_at = ?
      WHERE id = ? AND state = 'created' AND provider_checkout_id IS NULL AND create_attempt_started_at IS NULL`)
      .bind(new Date().toISOString(), intent.id).run();
    if (cancelled.meta?.changes === 1) return fetchAccountBilling(env, auth.uid);
    throw new BillingError('billing-checkout-in-progress', 409);
  }
  if (!billingConfig(env).providerReady) throw new BillingError('billing-unavailable');
  if (!intent.provider_checkout_id && typeof auth.email !== 'string') throw new BillingError('email-verification-required', 403);
  const resource = await recoverResource(env, intent, auth.email, options);
  await reconcile(env, intent, resource);
  if (!['cancelled', 'canceled'].includes(resource.status)) {
    await providerApi(env, `/preapproval/${encodeURIComponent(resource.id)}`, { ...options, method: 'PUT', body: { status: 'cancelled' } });
  }
  // Read after the mutation: a successful HTTP response alone does not confirm cancellation.
  const confirmed = await providerApi(env, `/preapproval/${encodeURIComponent(resource.id)}`, options);
  if (!['cancelled', 'canceled'].includes(confirmed.status)) throw new BillingError('billing-cancellation-unconfirmed');
  await reconcile(env, intent, confirmed);
  const remaining = await openIntent(env.TELEMETRY_DB, auth.uid);
  if (remaining) throw new BillingError('billing-cancellation-unconfirmed');
  return fetchAccountBilling(env, auth.uid);
}
