import { asaasApi, BILLING_PROVIDER, billingConfig, BillingError, checkoutUrl, OFFER_KEY,
  PROVIDER_ID, validateCheckoutUrl } from './asaasApi.js';
import { reconcileCheckout } from './asaasBilling.js';

async function openIntent(db, uid) {
  return db.prepare(`SELECT * FROM billing_checkout_intents WHERE account_uid = ? AND state <> 'cancelled' LIMIT 1`)
    .bind(uid).first();
}

export async function fetchAccountBilling(env, uid) {
  const config = billingConfig(env);
  const now = new Date().toISOString();
  const row = await env.TELEMETRY_DB.prepare(`
    SELECT i.state AS checkout_state, i.amount_cents, s.state,
      (SELECT MAX(p.period_end) FROM billing_payments p JOIN billing_subscriptions paid ON paid.id = p.subscription_id
       WHERE paid.account_uid = i.account_uid AND p.state = 'approved' AND p.refunded_cents = 0
         AND p.period_start <= ? AND p.period_end > ?) AS access_until
    FROM billing_checkout_intents i LEFT JOIN billing_subscriptions s ON s.checkout_intent_id = i.id
    WHERE i.account_uid = ? ORDER BY CASE WHEN i.state <> 'cancelled' THEN 0 ELSE 1 END, i.created_at DESC LIMIT 1
  `).bind(now, now, uid).first();
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
  const intent = await env.TELEMETRY_DB.prepare(`SELECT * FROM billing_checkout_intents
    WHERE account_uid = ? ORDER BY CASE WHEN state <> 'cancelled' THEN 0 ELSE 1 END, created_at DESC LIMIT 1`)
    .bind(auth.uid).first();
  if (!intent || intent.provider !== BILLING_PROVIDER || intent.state === 'cancelled') return;
  if (!intent.provider_checkout_id) {
    if (intent.create_attempt_started_at) throw new BillingError('billing-checkout-reconciliation-required', 409);
    return;
  }
  await reconcileCheckout(env, intent, options);
}

function firstDueDate() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'America/Sao_Paulo', year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23',
  }).formatToParts(new Date(Date.now() + 5 * 60_000));
  const value = name => parts.find(part => part.type === name)?.value;
  return `${value('year')}-${value('month')}-${value('day')} ${value('hour')}:${value('minute')}:${value('second')}`;
}

export async function createAccountCheckout(env, auth, payload, options = {}) {
  const config = billingConfig(env);
  if (!config.enabled) throw new BillingError('billing-unavailable');
  if (!auth.emailVerified) throw new BillingError('email-verification-required', 403);
  if (!payload || Object.keys(payload).length !== 1 || typeof payload.offerKey !== 'string') {
    throw new BillingError('invalid-offer', 400);
  }
  const db = env.TELEMETRY_DB;
  if (!await db.prepare('SELECT uid FROM account_profiles WHERE uid = ?').bind(auth.uid).first()) {
    throw new BillingError('profile-not-found', 404);
  }
  let intent = await openIntent(db, auth.uid);
  const expectedOfferKey = intent ? `${OFFER_KEY}_${intent.amount_cents}` : config.offer.key;
  if (payload.offerKey !== expectedOfferKey) throw new BillingError('invalid-offer', 400);
  if (!intent) {
    if ((await fetchAccountBilling(env, auth.uid)).subscription?.accessUntil) {
      throw new BillingError('billing-access-active', 409);
    }
    const id = crypto.randomUUID(); const now = new Date().toISOString();
    await db.prepare(`INSERT INTO billing_checkout_intents
      (id, account_uid, provider, external_reference, offer_key, amount_cents, currency, state, created_at, updated_at)
      VALUES (?, ?, ?, ?, ?, ?, 'BRL', 'created', ?, ?) ON CONFLICT DO NOTHING`)
      .bind(id, auth.uid, BILLING_PROVIDER, crypto.randomUUID(), OFFER_KEY, config.offer.amountCents, now, now).run();
    intent = await openIntent(db, auth.uid);
  }
  if (!intent || intent.provider !== BILLING_PROVIDER) throw new BillingError('billing-temporarily-unavailable');
  if (intent.provider_checkout_id) {
    if (intent.state !== 'pending') throw new BillingError('billing-subscription-exists', 409);
    const url = checkoutUrl(intent.provider_checkout_id);
    if (!url) throw new BillingError('invalid-checkout-url');
    return { checkoutUrl: url };
  }
  if (intent.create_attempt_started_at) throw new BillingError('billing-checkout-reconciliation-required', 409);
  const claimed = await db.prepare(`UPDATE billing_checkout_intents SET create_attempt_started_at = ?
    WHERE id = ? AND state = 'created' AND create_attempt_started_at IS NULL AND provider_checkout_id IS NULL`)
    .bind(new Date().toISOString(), intent.id).run();
  if (claimed.meta?.changes !== 1) throw new BillingError('billing-checkout-in-progress', 409);
  const checkout = await asaasApi(env, '/checkouts', { ...options, method: 'POST', body: {
    billingTypes: ['CREDIT_CARD'], chargeTypes: ['RECURRENT'], minutesToExpire: 60,
    externalReference: intent.external_reference,
    callback: { successUrl: config.returnUrl, cancelUrl: config.returnUrl, expiredUrl: config.returnUrl },
    items: [{ name: 'Ralven Pro', description: 'Assinatura mensal do Ralven Pro', quantity: 1,
      value: intent.amount_cents / 100 }],
    subscription: { cycle: 'MONTHLY', nextDueDate: firstDueDate() },
  } });
  if (!PROVIDER_ID.test(checkout.id ?? '')) throw new BillingError('invalid-provider-response');
  const updated = await db.prepare(`UPDATE billing_checkout_intents SET provider_checkout_id = ?, state = 'pending', updated_at = ?
    WHERE id = ? AND provider = ? AND provider_checkout_id IS NULL`)
    .bind(checkout.id, new Date().toISOString(), intent.id, BILLING_PROVIDER).run();
  if (updated.meta?.changes !== 1) throw new BillingError('billing-checkout-reconciliation-required', 409);
  const url = checkoutUrl(checkout.id);
  if (!validateCheckoutUrl(url)) throw new BillingError('invalid-checkout-url');
  return { checkoutUrl: url };
}

export async function cancelAccountBilling(env, auth, options = {}) {
  const db = env.TELEMETRY_DB;
  const intent = await openIntent(db, auth.uid);
  if (!intent) return fetchAccountBilling(env, auth.uid);
  if (!intent.provider_checkout_id && !intent.create_attempt_started_at) {
    await db.prepare(`UPDATE billing_checkout_intents SET state = 'cancelled', updated_at = ?
      WHERE id = ? AND state = 'created'`).bind(new Date().toISOString(), intent.id).run();
    return fetchAccountBilling(env, auth.uid);
  }
  if (!billingConfig(env).providerReady) throw new BillingError('billing-unavailable');
  if (!intent.provider_checkout_id) throw new BillingError('billing-checkout-reconciliation-required', 409);
  const subscription = await db.prepare(`SELECT provider_subscription_id FROM billing_subscriptions
    WHERE checkout_intent_id = ? AND provider = ?`).bind(intent.id, BILLING_PROVIDER).first();
  if (subscription?.provider_subscription_id) {
    await asaasApi(env, `/subscriptions/${encodeURIComponent(subscription.provider_subscription_id)}`,
      { ...options, method: 'DELETE', allowEmpty: true });
    const now = new Date().toISOString();
    await db.prepare(`UPDATE billing_subscriptions SET state = 'cancelled', provider_updated_at = ?, updated_at = ?
      WHERE checkout_intent_id = ?`).bind(now, now, intent.id).run();
  } else {
    await asaasApi(env, `/checkouts/${encodeURIComponent(intent.provider_checkout_id)}/cancel`,
      { ...options, method: 'POST', allowEmpty: true });
  }
  await db.prepare(`UPDATE billing_checkout_intents SET state = 'cancelled', updated_at = ? WHERE id = ?`)
    .bind(new Date().toISOString(), intent.id).run();
  return fetchAccountBilling(env, auth.uid);
}
