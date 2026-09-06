import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { createAccountCheckout, cancelAccountBilling, fetchAccountBilling, syncAccountBilling } from '../../src/billing/accountBilling.js';
import { billingConfig, checkoutUrl } from '../../src/billing/mercadoPagoApi.js';
import { reconcileAuthorizedPayment, monthlyPeriodEnd, handleMercadoPagoPaymentWebhook } from '../../src/billing/mercadoPagoPayments.js';
import { fetchAccountEntitlements } from '../../src/billing/entitlements.js';
import { deleteAccountProfile } from '../../src/auth/accountProfile.js';
import { handleMercadoPagoNotification } from '../../src/billing/mercadoPagoNotifications.js';
import worker from '../../src/index.js';
import { clearFirebaseJwksCache, FIREBASE_PROJECT_ID, FIREBASE_ID_TOKEN_ISSUER, FIREBASE_JWKS_URL } from '../../src/auth/firebaseIdToken.js';
import { generateKeyPairSync, sign } from 'node:crypto';

function setup(t) {
  const sqlite = new DatabaseSync(':memory:');
  t.after(() => sqlite.close());
  sqlite.exec("PRAGMA foreign_keys=ON; CREATE TABLE account_profiles(uid TEXT PRIMARY KEY); INSERT INTO account_profiles VALUES('user-1'),('user-2');");
  for (const name of ['0007_billing_foundation.sql', '0009_billing_checkout_payments.sql']) {
    sqlite.exec(readFileSync(new URL(`../../migrations/${name}`, import.meta.url), 'utf8'));
  }
  const db = {
    prepare(sql) {
      return { bind(...args) { return {
        first: async () => sqlite.prepare(sql).get(...args) ?? null,
        run: async () => ({ meta: { changes: Number(sqlite.prepare(sql).run(...args).changes) } }),
      }; } };
    },
    async batch(statements) {
      sqlite.exec('BEGIN IMMEDIATE');
      try { const result = []; for (const statement of statements) result.push(await statement.run()); sqlite.exec('COMMIT'); return result; }
      catch (error) { sqlite.exec('ROLLBACK'); throw error; }
    },
  };
  const env = { TELEMETRY_DB: db, MERCADO_PAGO_ACCESS_TOKEN: 'test-only-access', MERCADO_PAGO_WEBHOOK_SECRET: 'test-only-secret',
    MERCADO_PAGO_BILLING_ENABLED: 'true', MERCADO_PAGO_RETURN_URL: 'https://example.test/billing/return' };
  const auth = { uid: 'user-1', emailVerified: true, email: 'buyer@example.test' };
  let mandate = null;
  let paymentStatus = 'approved';
  let refunded = 0;
  let revision = 0;
  let invoiceId = '100';
  let paymentId = '200';
  const now = new Date();
  let debitDate = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString();
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, ...init });
    const path = new URL(url).pathname;
    if (path === '/preapproval' && init.method === 'POST') {
      const body = JSON.parse(init.body);
      mandate = { ...body, id: 'mandate-1', last_modified: '2026-01-01T00:00:00.000Z',
        init_point: 'https://www.mercadopago.com.br/subscriptions/checkout?preapproval_id=mandate-1' };
    } else if (path === '/preapproval/mandate-1' && init.method === 'PUT') {
      mandate = { ...mandate, status: 'cancelled', last_modified: '2026-01-02T00:00:00.000Z' };
    }
    let body = mandate;
    if (path === '/preapproval/search') body = { results: mandate ? [mandate] : [] };
    if (path.startsWith('/authorized_payments/')) body = { id: invoiceId, preapproval_id: 'mandate-1',
      debit_date: debitDate, transaction_amount: '19.90', currency_id: 'BRL', payment: { id: paymentId, status: 'approved' } };
    if (path === '/authorized_payments/search') body = { results: [body], paging: { offset: 0, limit: 100, total: 1 } };
    if (path.startsWith('/v1/payments/')) body = { id: paymentId, status: paymentStatus, transaction_amount: 19.9,
      transaction_amount_refunded: refunded, currency_id: 'BRL', date_approved: debitDate,
      date_last_updated: new Date(Date.UTC(2026, 0, 1, 0, revision)).toISOString() };
    return Response.json(body);
  };
  return { sqlite, db, env, auth, calls, options: { fetchImpl },
    checkout: () => createAccountCheckout(env, auth, { offerKey: 'ralven_pro_monthly_1990' }, { fetchImpl }),
    setPayment(status, refund = 0) { paymentStatus = status; refunded = refund; revision++; },
    setRevision(value) { revision = value; },
    setInvoice(id, payment, date) { invoiceId = id; paymentId = payment; debitDate = date; },
    mutateMandate(change) { Object.assign(mandate, change); },
  };
}

test('checkout is disabled until configured and URL validation rejects lookalikes and credentials', () => {
  assert.equal(billingConfig({}).enabled, false);
  assert.equal(billingConfig({ MERCADO_PAGO_AMOUNT_CENTS: 'NaN' }).enabled, false);
  assert.equal(billingConfig({ MERCADO_PAGO_AMOUNT_CENTS: '100001' }).enabled, false);
  for (const url of ['http://www.mercadopago.com.br/subscriptions/checkout?preapproval_id=x',
    'https://www.mercadopago.com.br.evil.test/subscriptions/checkout?preapproval_id=x',
    'https://user@www.mercadopago.com.br/subscriptions/checkout?preapproval_id=x',
    'https://www.mercadopago.com.br/other?preapproval_id=x']) assert.equal(checkoutUrl(url), null);
});

test('checkout uses server price and verified account, resumes the same pending mandate', async t => {
  const f = setup(t);
  await assert.rejects(createAccountCheckout(f.env, f.auth, { offerKey: 'ralven_pro_monthly_1990', amountCents: 1 }, f.options), /invalid-offer/);
  const checkout = await f.checkout();
  assert.match(checkout.checkoutUrl, /preapproval_id=mandate-1$/);
  assert.deepEqual(await f.checkout(), checkout);
  assert.equal(f.calls.filter(call => call.method === 'POST').length, 1);
  const body = JSON.parse(f.calls[0].body);
  assert.equal(body.auto_recurring.transaction_amount, 19.9);
  assert.equal(body.payer_email, f.auth.email);
  assert.notEqual(body.external_reference, f.auth.uid);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  const snapshot = await fetchAccountBilling(f.env, f.auth.uid);
  assert.equal(snapshot.subscription.state, 'pending');
  assert.doesNotMatch(JSON.stringify(snapshot), /mandate-1|buyer@|user-1/);
});

test('concurrent checkout calls create only one mandate', async t => {
  const f = setup(t);
  const results = await Promise.allSettled([f.checkout(), f.checkout(), f.checkout()]);
  assert.ok(results.some(result => result.status === 'fulfilled'));
  assert.equal(f.calls.filter(call => call.method === 'POST').length, 1);
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_checkout_intents').get().n, 1);
});

test('price-versioned consent rejects stale offers and preserves a pending mandate price', async t => {
  const f = setup(t);
  await assert.rejects(createAccountCheckout({ ...f.env, MERCADO_PAGO_AMOUNT_CENTS: '2990' }, f.auth,
    { offerKey: 'ralven_pro_monthly_1990' }, f.options), /invalid-offer/);
  assert.equal(f.calls.length, 0);
  await f.checkout();
  const snapshot = await fetchAccountBilling({ ...f.env, MERCADO_PAGO_AMOUNT_CENTS: '2990' }, f.auth.uid);
  assert.equal(snapshot.offer.key, 'ralven_pro_monthly_1990');
  assert.equal(snapshot.offer.amountCents, 1990);
  assert.ok(snapshot.checkoutAvailable);
});

test('ambiguous create is recovered by opaque reference; an empty recovery never recreates a mandate', async t => {
  const f = setup(t);
  let fail = true;
  const options = { fetchImpl: async (url, init) => {
    const response = await f.options.fetchImpl(url, init);
    if (init.method === 'POST' && fail) { fail = false; throw new Error('timeout after provider commit'); }
    return response;
  } };
  await assert.rejects(createAccountCheckout(f.env, f.auth, { offerKey: 'ralven_pro_monthly_1990' }, options), /provider-temporarily-unavailable/);
  assert.ok((await createAccountCheckout(f.env, f.auth, { offerKey: 'ralven_pro_monthly_1990' }, options)).checkoutUrl);
  assert.equal(f.calls.filter(call => call.method === 'POST').length, 1);
  f.sqlite.prepare("UPDATE billing_checkout_intents SET provider_checkout_id=NULL WHERE account_uid='user-1'").run();
  await assert.rejects(createAccountCheckout(f.env, f.auth, { offerKey: 'ralven_pro_monthly_1990' },
    { fetchImpl: async () => Response.json({ results: [] }) }), /billing-checkout-reconciliation-required/);
});

test('calendar periods clamp month ends including leap year', () => {
  assert.equal(monthlyPeriodEnd('2028-01-31T10:00:00.000Z'), '2028-02-29T10:00:00.000Z');
  assert.equal(monthlyPeriodEnd('2027-01-31T10:00:00.000Z'), '2027-02-28T10:00:00.000Z');
  assert.equal(monthlyPeriodEnd('2026-12-15T10:00:00.000Z'), '2027-01-15T10:00:00.000Z');
});

test('only approved canonical payment grants a stable paid period; replay does not extend access', async t => {
  const f = setup(t); await f.checkout();
  f.mutateMandate({ status: 'authorized', last_modified: '2026-01-01T00:01:00.000Z' });
  f.setPayment('rejected');
  await reconcileAuthorizedPayment(f.env, '100', 'request-1', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  f.setPayment('approved');
  await reconcileAuthorizedPayment(f.env, '100', 'request-2', f.options);
  const original = await fetchAccountEntitlements(f.db, f.auth.uid);
  assert.equal(original.tier, 'pro');
  await reconcileAuthorizedPayment(f.env, '100', 'request-2', f.options);
  assert.deepEqual(await fetchAccountEntitlements(f.db, f.auth.uid), original);
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_payments').get().n, 1);
});

test('partial refund revokes its paid period and stale approval cannot restore it', async t => {
  const f = setup(t); await f.checkout();
  await reconcileAuthorizedPayment(f.env, '100', 'approved', f.options);
  f.setPayment('approved', 5);
  await reconcileAuthorizedPayment(f.env, '100', 'refund', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  f.setPayment('approved'); f.setRevision(0);
  await reconcileAuthorizedPayment(f.env, '100', 'stale', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
});

test('confirmed cancellation stops renewal, retains paid access, and permits account deletion', async t => {
  const f = setup(t); await f.checkout();
  await reconcileAuthorizedPayment(f.env, '100', 'approved', f.options);
  assert.equal(await deleteAccountProfile(f.db, f.auth.uid), false);
  const result = await cancelAccountBilling(f.env, f.auth, f.options);
  assert.equal(result.subscription.state, 'cancelled');
  assert.equal(result.subscription.canCancel, false);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'pro');
  assert.equal(result.checkoutAvailable, false);
  await assert.rejects(f.checkout(), /billing-access-active/);
  assert.equal(await deleteAccountProfile(f.db, f.auth.uid), true);
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_payments').get().n, 0);
});

test('account refresh recovers a missed approved-payment webhook', async t => {
  const f = setup(t); await f.checkout();
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  f.mutateMandate({ auto_recurring: { frequency: 1, frequency_type: 'months', transaction_amount: '19.90', currency_id: 'BRL' } });
  await syncAccountBilling(f.env, f.auth, f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'pro');
  assert.ok(f.calls.some(call => call.url.includes('/authorized_payments/search?preapproval_id=mandate-1')));
});

test('a successful retry of one invoice can have a different payment ID', async t => {
  const f = setup(t); await f.checkout();
  f.setPayment('rejected');
  await reconcileAuthorizedPayment(f.env, '100', 'declined-attempt', f.options);
  const periodStart = f.sqlite.prepare('SELECT period_start FROM billing_payments').get().period_start;
  f.setInvoice('100', '201', periodStart);
  f.setPayment('approved');
  await reconcileAuthorizedPayment(f.env, '100', 'approved-attempt', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'pro');
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_payments').get().n, 2);
});

test('an older-attempt payment notification rechecks the signed payment, not the invoice latest payment', async t => {
  const f = setup(t); await f.checkout();
  await reconcileAuthorizedPayment(f.env, '100', 'first-attempt', f.options);
  const periodStart = f.sqlite.prepare('SELECT period_start FROM billing_payments').get().period_start;
  f.setInvoice('100', '201', periodStart);
  f.setPayment('refunded', 19.9);
  const options = { ...f.options, expectedPaymentId: '200', fetchImpl: async (url, init) => {
    const response = await f.options.fetchImpl(url, init);
    const body = await response.json();
    if (url.endsWith('/v1/payments/200')) body.id = '200';
    return Response.json(body);
  } };
  await reconcileAuthorizedPayment(f.env, '100', 'refund-old-attempt', options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  assert.ok(f.calls.at(-2).url.endsWith('/v1/payments/200'));
});

test('failed cancellation preserves billing linkage and blocks account deletion', async t => {
  const f = setup(t); await f.checkout();
  await assert.rejects(cancelAccountBilling(f.env, f.auth, { fetchImpl: async (url, init) =>
    init.method === 'PUT' ? new Response('', { status: 500 }) : f.options.fetchImpl(url, init) }), /provider-temporarily-unavailable/);
  assert.equal(await deleteAccountProfile(f.db, f.auth.uid), false);
});

test('an unattempted intent can be cancelled locally without touching the provider', async t => {
  const f = setup(t);
  const now = new Date().toISOString();
  f.sqlite.prepare(`INSERT INTO billing_checkout_intents
    (id,account_uid,provider,external_reference,offer_key,amount_cents,currency,state,created_at,updated_at)
    VALUES ('unattempted','user-1','mercado_pago','ref-unattempted','ralven_pro_monthly',1990,'BRL','created',?,?)`).run(now, now);
  const result = await cancelAccountBilling(f.env, f.auth, f.options);
  assert.equal(result.subscription.state, 'cancelled');
  assert.equal(result.subscription.canCancel, false);
  assert.equal(f.calls.length, 0);
  assert.equal(await deleteAccountProfile(f.db, f.auth.uid), true);
});

test('future paid renewal activates only at its own period boundary', async t => {
  const f = setup(t); await f.checkout();
  const now = new Date();
  const start = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1)).toISOString();
  f.setInvoice('101', '201', start);
  await reconcileAuthorizedPayment(f.env, '101', 'future', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  assert.equal(f.sqlite.prepare('SELECT period_start FROM billing_payments').get().period_start, start);
});

test('payment price mismatch and ownership conflicts cannot grant another account', async t => {
  const f = setup(t); await f.checkout();
  await assert.rejects(reconcileAuthorizedPayment(f.env, '100', 'bad-price', { fetchImpl: async (url, init) => {
    const response = await f.options.fetchImpl(url, init);
    const body = await response.json();
    if (url.includes('/v1/payments/')) body.transaction_amount = 1;
    return Response.json(body);
  } }), /billing-payment-mismatch/);
  assert.equal((await fetchAccountEntitlements(f.db, 'user-1')).tier, 'free');
  assert.equal((await fetchAccountEntitlements(f.db, 'user-2')).tier, 'free');
});

test('approved payment without explicit refund amount fails closed', async t => {
  const f = setup(t); await f.checkout();
  await assert.rejects(reconcileAuthorizedPayment(f.env, '100', 'missing-refund', { fetchImpl: async (url, init) => {
    const body = await (await f.options.fetchImpl(url, init)).json();
    if (url.includes('/v1/payments/')) delete body.transaction_amount_refunded;
    return Response.json(body);
  } }), /invalid-provider-response/);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
});

test('payment webhook verifies HMAC before any provider fetch or database write', async t => {
  const f = setup(t);
  const result = await handleMercadoPagoPaymentWebhook(new Request(
    'https://worker.test/billing/mercado-pago/webhook?type=subscription_authorized_payment&data.id=100',
    { method: 'POST', headers: { 'x-request-id': 'request-1', 'x-signature': `ts=1,v1=${'0'.repeat(64)}` } }), f.env, f.options);
  assert.equal(result.status, 401);
  assert.equal(f.calls.length, 0);
});

async function signedPaymentRequest(env, paymentId, requestId = 'payment-first') {
  const timestamp = String(Date.now());
  const key = await crypto.subtle.importKey('raw', new TextEncoder().encode(env.MERCADO_PAGO_WEBHOOK_SECRET),
    { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']);
  const signature = await crypto.subtle.sign('HMAC', key,
    new TextEncoder().encode(`id:${paymentId};request-id:${requestId};ts:${timestamp};`));
  return new Request(`https://worker.test/billing/mercado-pago/webhook?type=payment&data.id=${paymentId}`, {
    method: 'POST', headers: { 'x-request-id': requestId, 'x-signature': `ts=${timestamp},v1=${Buffer.from(signature).toString('hex')}` },
  });
}

test('signed payment notification arriving before invoice notification resolves the canonical invoice', async t => {
  const f = setup(t); await f.checkout();
  const response = await handleMercadoPagoPaymentWebhook(await signedPaymentRequest(f.env, '200'), f.env, f.options);
  assert.equal(response.status, 200);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'pro');
  assert.ok(f.calls.some(call => call.url.includes('/authorized_payments/search?payment_id=200')));
});

test('signed subscription body topic is only a bounded routing hint tied to the signed resource', async t => {
  const f = setup(t); await f.checkout();
  const signed = await signedPaymentRequest(f.env, '100', 'body-topic');
  const request = body => new Request('https://worker.test/billing/mercado-pago/webhook?data.id=100', {
    method: 'POST', headers: signed.headers, body: JSON.stringify(body),
  });
  const mismatch = await handleMercadoPagoNotification(request({ type: 'subscription_authorized_payment', data: { id: 'other' } }), f.env, f.options);
  assert.equal(mismatch.status, 400);
  const response = await handleMercadoPagoNotification(request({ type: 'subscription_authorized_payment', data: { id: 100 }, status: 'approved' }), f.env, f.options);
  assert.equal(response.status, 200);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'pro');
  const oversized = new Request('https://worker.test/billing/mercado-pago/webhook?data.id=100', {
    method: 'POST', headers: signed.headers, body: JSON.stringify({ type: 'payment', data: { id: 100 }, unused: 'x'.repeat(9000) }),
  });
  assert.equal((await handleMercadoPagoNotification(oversized, f.env, f.options)).status, 400);
});

test('payment commit timeout is retry-safe and does not extend the paid period', async t => {
  const f = setup(t); await f.checkout();
  const originalBatch = f.db.batch;
  f.db.batch = async statements => {
    const result = await originalBatch(statements);
    if (statements.length === 5) { f.db.batch = originalBatch; throw new Error('timeout after payment commit'); }
    return result;
  };
  await assert.rejects(reconcileAuthorizedPayment(f.env, '100', 'payment-commit', f.options), /timeout/);
  const paid = await fetchAccountEntitlements(f.db, f.auth.uid);
  await reconcileAuthorizedPayment(f.env, '100', 'payment-commit', f.options);
  assert.deepEqual(await fetchAccountEntitlements(f.db, f.auth.uid), paid);
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_payments').get().n, 1);
});

test('Worker billing routes authenticate, enforce the binding and consent, and keep provider IDs private', async t => {
  const f = setup(t);
  clearFirebaseJwksCache();
  t.after(clearFirebaseJwksCache);
  const { privateKey, publicKey } = generateKeyPairSync('rsa', { modulusLength: 2048 });
  const encoded = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const now = Math.floor(Date.now() / 1000);
  const unsigned = `${encoded({ alg: 'RS256', kid: 'billing-test' })}.${encoded({
    aud: FIREBASE_PROJECT_ID, iss: FIREBASE_ID_TOKEN_ISSUER, sub: f.auth.uid,
    exp: now + 300, iat: now - 10, auth_time: now - 10, email_verified: true, email: f.auth.email,
  })}`;
  const token = `${unsigned}.${sign('RSA-SHA256', Buffer.from(unsigned), privateKey).toString('base64url')}`;
  t.mock.method(globalThis, 'fetch', async (url, init) => url === FIREBASE_JWKS_URL
    ? Response.json({ keys: [{ ...publicKey.export({ format: 'jwk' }), kid: 'billing-test', alg: 'RS256', use: 'sig' }] })
    : f.options.fetchImpl(url, init));
  const request = (path, method = 'GET', body) => new Request(`https://worker.test${path}`, {
    method, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json; charset=utf-8' },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  assert.equal((await worker.fetch(new Request('https://worker.test/account/billing'), f.env)).status, 401);
  assert.equal((await worker.fetch(request('/account/billing/checkout', 'POST', { offerKey: 'ralven_pro_monthly_1990' }), f.env)).status, 429);
  const limiter = { limit: async ({ key }) => { assert.equal(key, 'billing:user-1'); return { success: true }; } };
  f.env.BILLING_WRITE_LIMITER = limiter; f.env.BILLING_READ_LIMITER = limiter;
  assert.equal((await worker.fetch(request('/account/billing/checkout', 'POST',
    { offerKey: 'ralven_pro_monthly_1990', uid: 'user-2' }), f.env)).status, 400);
  const checkout = await worker.fetch(request('/account/billing/checkout', 'POST', { offerKey: 'ralven_pro_monthly_1990' }), f.env);
  assert.equal(checkout.status, 200);
  const billing = await worker.fetch(request('/account/billing'), f.env);
  assert.equal(billing.status, 200);
  assert.doesNotMatch(await billing.text(), /mandate-1|buyer@|user-1/);
  assert.equal(billing.headers.get('Cache-Control'), 'no-store');
  const access = await worker.fetch(request('/account/entitlements'), f.env);
  assert.equal((await access.json()).tier, 'pro');
});

test('public browser return never echoes payment references, tokens or malicious query and never claims approval', async () => {
  const response = await worker.fetch(new Request(
    'https://worker.test/billing/return?status=approved&preapproval_id=secret-mandate&token=secret-token&message=%3Cscript%3Ealert(1)%3C/script%3E'), {});
  assert.equal(response.status, 200);
  assert.match(response.headers.get('Content-Type'), /^text\/html/);
  assert.equal(response.headers.get('Cache-Control'), 'no-store');
  assert.equal(response.headers.get('Referrer-Policy'), 'no-referrer');
  const csp = response.headers.get('Content-Security-Policy');
  assert.match(csp, /default-src 'none'/);
  assert.match(csp, /frame-ancestors 'none'/);
  assert.match(csp, /style-src 'nonce-[^']+'/);
  const html = await response.text();
  assert.match(html, /lang="pt-BR"/);
  assert.match(html, /Atualizar assinatura/);
  assert.match(html, /Esta página não confirma aprovação/);
  assert.match(html, /pagamento ainda pode estar pendente/);
  assert.doesNotMatch(html, /secret-mandate|secret-token|<script|alert\(1\)|status=approved/);
  assert.doesNotMatch(html, /pagamento (aprovado|confirmado)|assinatura ativada/i);
});
