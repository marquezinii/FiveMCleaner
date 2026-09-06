import { readBoundedJson } from '../requestSecurity.js';
import { verifyMercadoPagoSignature } from './mercadoPagoSignature.js';
import { cents } from './mercadoPagoApi.js';

const PROVIDER = 'mercado_pago';
const MAX_PROVIDER_BODY_BYTES = 32 * 1024;
const PROVIDER_TIMEOUT_MS = 10_000;
const OPAQUE_ID = /^[A-Za-z0-9_-]{1,128}$/;

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function parseProviderUpdatedAt(value) {
  if (typeof value !== 'string' || value.length > 64) {
    return null;
  }

  const milliseconds = Date.parse(value);
  return Number.isFinite(milliseconds) ? new Date(milliseconds).toISOString() : null;
}

export function parsePreapproval(value, resourceId) {
  if (value === null || typeof value !== 'object' || value.id !== resourceId
    || typeof value.external_reference !== 'string'
    || value.external_reference.length < 1 || value.external_reference.length > 128
    || value.auto_recurring === null || typeof value.auto_recurring !== 'object') {
    return null;
  }

  const status = value.status === 'canceled' ? 'cancelled' : value.status;
  if (!['pending', 'authorized', 'paused', 'cancelled'].includes(status)) {
    return null;
  }

  const currency = value.auto_recurring.currency_id;
  const amountCents = cents(value.auto_recurring.transaction_amount);
  const providerUpdatedAt = parseProviderUpdatedAt(value.last_modified);
  if (typeof currency !== 'string' || !/^[A-Z]{3}$/.test(currency)
    || amountCents === null || providerUpdatedAt === null) {
    return null;
  }

  return {
    externalReference: value.external_reference,
    currency,
    amountCents,
    providerUpdatedAt,
    state: status,
  };
}

function intentMatches(intent, preapproval, resourceId) {
  const resourceUnclaimed = intent !== null
    && intent.resource_account_uid === null
    && intent.resource_checkout_intent_id === null
    && intent.resource_offer_key === null;
  const resourceOwnedByIntent = intent !== null
    && intent.resource_account_uid === intent.account_uid
    && intent.resource_checkout_intent_id === intent.id
    && intent.resource_offer_key === intent.offer_key;

  return intent !== null
    && intent.provider === PROVIDER
    && intent.external_reference === preapproval.externalReference
    && intent.currency === preapproval.currency
    && intent.amount_cents === preapproval.amountCents
    && (intent.provider_checkout_id === null || intent.provider_checkout_id === resourceId)
    && (intent.intent_subscription_resource_id === null
      || intent.intent_subscription_resource_id === resourceId)
    && ['created', 'pending', 'completed', 'cancelled'].includes(intent.state)
    && typeof intent.id === 'string'
    && typeof intent.account_uid === 'string'
    && typeof intent.offer_key === 'string'
    && (resourceUnclaimed || resourceOwnedByIntent);
}

async function recordTerminalEvent(db, requestId, resourceId, receivedAt, outcome) {
  await db.prepare(
    `INSERT INTO billing_webhook_events
       (provider, provider_request_id, resource_id, received_at, processing_outcome, processed_at)
     VALUES (?, ?, ?, ?, ?, ?)
     ON CONFLICT(provider, provider_request_id) DO UPDATE SET
       processing_outcome = excluded.processing_outcome,
       processed_at = excluded.processed_at
     WHERE billing_webhook_events.resource_id = excluded.resource_id
       AND billing_webhook_events.processing_outcome IN ('pending', 'failed')`,
  ).bind(PROVIDER, requestId, resourceId, receivedAt, outcome, receivedAt).run();
}

function checkoutState(subscriptionState) {
  if (subscriptionState === 'pending') return 'pending';
  if (subscriptionState === 'cancelled') return 'cancelled';
  return 'completed';
}

async function persistPreapproval(db, intent, preapproval, requestId, resourceId, receivedAt) {
  const eventGuard = `EXISTS (
    SELECT 1 FROM billing_webhook_events
    WHERE provider = ? AND provider_request_id = ? AND resource_id = ?
      AND processing_outcome IN ('pending', 'failed')
  )`;

  const statements = [
    db.prepare(
      `INSERT INTO billing_webhook_events
         (provider, provider_request_id, resource_id, received_at, processing_outcome, processed_at)
       VALUES (?, ?, ?, ?, 'pending', NULL)
       ON CONFLICT(provider, provider_request_id) DO NOTHING`,
    ).bind(PROVIDER, requestId, resourceId, receivedAt),
    db.prepare(
      `UPDATE billing_checkout_intents
       SET provider_checkout_id = ?, state = ?, updated_at = ?
       WHERE id = ? AND account_uid = ? AND provider = ?
         AND external_reference = ? AND offer_key = ?
         AND amount_cents = ? AND currency = ?
         AND state IN ('created', 'pending', 'completed', 'cancelled')
         AND (provider_checkout_id IS NULL OR provider_checkout_id = ?)
         AND NOT EXISTS (
           SELECT 1 FROM billing_subscriptions
           WHERE provider = ? AND provider_subscription_id = ?
             AND (account_uid <> ? OR checkout_intent_id <> ? OR offer_key <> ?
               OR provider_updated_at >= ?)
         )
         AND ${eventGuard}`,
    ).bind(
      resourceId,
      checkoutState(preapproval.state),
      receivedAt,
      intent.id,
      intent.account_uid,
      PROVIDER,
      preapproval.externalReference,
      intent.offer_key,
      preapproval.amountCents,
      preapproval.currency,
      resourceId,
      PROVIDER,
      resourceId,
      intent.account_uid,
      intent.id,
      intent.offer_key,
      preapproval.providerUpdatedAt,
      PROVIDER,
      requestId,
      resourceId,
    ),
    db.prepare(
      `INSERT INTO billing_subscriptions
         (id, account_uid, checkout_intent_id, provider, provider_subscription_id,
          offer_key, state, provider_updated_at, last_event_id, created_at, updated_at)
       SELECT ?, i.account_uid, i.id, i.provider, ?, i.offer_key, ?, ?,
         (SELECT id FROM billing_webhook_events
          WHERE provider = ? AND provider_request_id = ? AND resource_id = ?),
         ?, ?
       FROM billing_checkout_intents i
       WHERE i.id = ? AND i.account_uid = ? AND i.provider = ?
         AND i.external_reference = ? AND i.offer_key = ?
         AND i.amount_cents = ? AND i.currency = ?
         AND i.state IN ('created', 'pending', 'completed', 'cancelled')
         AND (i.provider_checkout_id IS NULL OR i.provider_checkout_id = ?)
         AND ${eventGuard}
       ON CONFLICT(provider, provider_subscription_id) DO UPDATE SET
         state = excluded.state,
         provider_updated_at = excluded.provider_updated_at,
         last_event_id = excluded.last_event_id,
         updated_at = excluded.updated_at
       WHERE billing_subscriptions.account_uid = excluded.account_uid
         AND billing_subscriptions.checkout_intent_id = excluded.checkout_intent_id
         AND billing_subscriptions.offer_key = excluded.offer_key
         AND billing_subscriptions.provider_updated_at < excluded.provider_updated_at`,
    ).bind(
      `${PROVIDER}:${resourceId}`,
      resourceId,
      preapproval.state,
      preapproval.providerUpdatedAt,
      PROVIDER,
      requestId,
      resourceId,
      receivedAt,
      receivedAt,
      intent.id,
      intent.account_uid,
      PROVIDER,
      preapproval.externalReference,
      intent.offer_key,
      preapproval.amountCents,
      preapproval.currency,
      resourceId,
      PROVIDER,
      requestId,
      resourceId,
    ),
    db.prepare(
      `UPDATE billing_webhook_events
       SET processing_outcome = 'processed', processed_at = ?
       WHERE provider = ? AND provider_request_id = ? AND resource_id = ?
         AND processing_outcome IN ('pending', 'failed')
         AND EXISTS (
           SELECT 1 FROM billing_subscriptions
           WHERE provider = ? AND provider_subscription_id = ?
             AND account_uid = ? AND checkout_intent_id = ? AND offer_key = ?
         )`,
    ).bind(
      receivedAt,
      PROVIDER,
      requestId,
      resourceId,
      PROVIDER,
      resourceId,
      intent.account_uid,
      intent.id,
      intent.offer_key,
    ),
  ];

  await db.batch(statements);
  const event = await db.prepare(
    `SELECT processing_outcome
     FROM billing_webhook_events
     WHERE provider = ? AND provider_request_id = ? AND resource_id = ?`,
  ).bind(PROVIDER, requestId, resourceId).first();
  return event?.processing_outcome === 'processed';
}

/**
 * Reconciles a signed Mercado Pago preapproval notification against an opaque
 * checkout intent. The webhook body is deliberately never read or trusted.
 */
export async function handleMercadoPagoWebhook(request, env, options = {}) {
  let url;
  try {
    url = new URL(request.url);
  } catch {
    return json({ error: 'invalid-webhook' }, 400);
  }

  const resourceIds = url.searchParams.getAll('data.id');
  const resourceId = resourceIds[0];
  const requestId = request.headers.get('x-request-id');
  const signature = request.headers.get('x-signature');
  if (resourceIds.length !== 1 || !OPAQUE_ID.test(resourceId ?? '')
    || !OPAQUE_ID.test(requestId ?? '') || typeof signature !== 'string'
    || signature.length === 0 || signature.length > 256) {
    return json({ error: 'invalid-webhook' }, 400);
  }

  const db = env?.TELEMETRY_DB;
  const accessToken = env?.MERCADO_PAGO_ACCESS_TOKEN;
  const webhookSecret = env?.MERCADO_PAGO_WEBHOOK_SECRET;
  const fetchImpl = options.fetchImpl ?? globalThis.fetch;
  if (typeof db?.prepare !== 'function' || typeof db?.batch !== 'function'
    || typeof accessToken !== 'string' || accessToken.length === 0
    || typeof webhookSecret !== 'string' || webhookSecret.length === 0
    || typeof fetchImpl !== 'function') {
    return json({ error: 'billing-unavailable' }, 503);
  }

  const signatureValid = await verifyMercadoPagoSignature({
    requestUrl: request.url,
    signatureHeader: signature,
    requestId,
    secret: webhookSecret,
  });
  if (!signatureValid) {
    return json({ error: 'invalid-signature' }, 401);
  }

  const receivedAt = new Date().toISOString();

  let existingEvent;
  try {
    existingEvent = await db.prepare(
      `SELECT resource_id, processing_outcome
       FROM billing_webhook_events
       WHERE provider = ? AND provider_request_id = ?`,
    ).bind(PROVIDER, requestId).first();
  } catch {
    return json({ error: 'billing-temporarily-unavailable' }, 503);
  }

  if (existingEvent !== null) {
    if (existingEvent.resource_id !== resourceId) {
      return json({ error: 'request-id-conflict' }, 409);
    }
    if (['processed', 'ignored'].includes(existingEvent.processing_outcome)) {
      return json({ accepted: true });
    }
  }

  let providerResponse;
  try {
    providerResponse = await fetchImpl(
      `https://api.mercadopago.com/preapproval/${encodeURIComponent(resourceId)}`,
      {
        method: 'GET',
        headers: { Accept: 'application/json', Authorization: `Bearer ${accessToken}` },
        redirect: 'error',
        signal: AbortSignal.timeout(PROVIDER_TIMEOUT_MS),
      },
    );
  } catch {
    return json({ error: 'provider-temporarily-unavailable' }, 503);
  }

  if (!providerResponse.ok) {
    return json({ error: 'provider-temporarily-unavailable' }, 503);
  }

  const providerPayload = await readBoundedJson(providerResponse, MAX_PROVIDER_BODY_BYTES);
  const preapproval = parsePreapproval(providerPayload, resourceId);
  if (preapproval === null) {
    return json({ error: 'invalid-provider-response' }, 503);
  }

  return reconcilePreapproval(db, preapproval, requestId, resourceId, receivedAt);
}

export async function reconcilePreapproval(db, preapproval, requestId, resourceId, receivedAt = new Date().toISOString()) {
  let intent;
  try {
    intent = await db.prepare(
      `SELECT i.id, i.account_uid, i.provider, i.external_reference, i.offer_key,
              i.amount_cents, i.currency, i.provider_checkout_id, i.state,
              intent_subscription.provider_updated_at AS subscription_provider_updated_at,
              intent_subscription.provider_subscription_id AS intent_subscription_resource_id,
              resource_subscription.account_uid AS resource_account_uid,
              resource_subscription.checkout_intent_id AS resource_checkout_intent_id,
              resource_subscription.offer_key AS resource_offer_key
       FROM billing_checkout_intents i
       LEFT JOIN billing_subscriptions intent_subscription
         ON intent_subscription.checkout_intent_id = i.id
       LEFT JOIN billing_subscriptions resource_subscription
         ON resource_subscription.provider = ?
        AND resource_subscription.provider_subscription_id = ?
       WHERE i.provider = ? AND i.external_reference = ?
       LIMIT 1`,
    ).bind(PROVIDER, resourceId, PROVIDER, preapproval.externalReference).first();

    if (!intentMatches(intent, preapproval, resourceId)) {
      await recordTerminalEvent(db, requestId, resourceId, receivedAt, 'ignored');
      return json({ accepted: true });
    }

    if (typeof intent.subscription_provider_updated_at === 'string'
      && intent.subscription_provider_updated_at >= preapproval.providerUpdatedAt) {
      await recordTerminalEvent(db, requestId, resourceId, receivedAt, 'processed');
      return json({ accepted: true });
    }

    const processed = await persistPreapproval(
      db,
      intent,
      preapproval,
      requestId,
      resourceId,
      receivedAt,
    );
    return processed
      ? json({ accepted: true })
      : json({ error: 'billing-temporarily-unavailable' }, 503);
  } catch {
    return json({ error: 'billing-temporarily-unavailable' }, 503);
  }
}
