import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import { createAccountCheckout, cancelAccountBilling, fetchAccountBilling, syncAccountBilling } from '../../src/billing/accountBilling.js';
import { billingConfig, validateCheckoutUrl } from '../../src/billing/asaasApi.js';
import { handleAsaasWebhook, reconcilePayment } from '../../src/billing/asaasBilling.js';
import { fetchAccountEntitlements } from '../../src/billing/entitlements.js';
import { deleteAccountProfile } from '../../src/auth/accountProfile.js';
import worker from '../../src/index.js';

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
  const env = {
    TELEMETRY_DB: db,
    ASAAS_ACCESS_TOKEN: '$aact_hmlg_test-only-access',
    ASAAS_WEBHOOK_TOKEN: 'test-only-webhook-token-32-chars!',
    ASAAS_ENVIRONMENT: 'sandbox',
    ASAAS_BILLING_ENABLED: 'true',
    ASAAS_RETURN_URL: 'https://example.test/billing/return',
  };
  const auth = { uid: 'user-1', emailVerified: true, email: 'buyer@example.test' };
  let checkoutCreated = false;
  let paymentStatus = 'CONFIRMED';
  let refunds = null;
  let subscriptionStatus = 'ACTIVE';
  let chargeback = null;
  const calls = [];
  const fetchImpl = async (url, init) => {
    calls.push({ url, ...init });
    const parsed = new URL(url);
    const path = parsed.pathname.replace(/^\/v3/, '');
    if (path === '/checkouts' && init.method === 'POST') {
      checkoutCreated = true;
      return Response.json({ id: 'checkout-1' });
    }
    if (path === '/payments' && parsed.searchParams.get('checkoutSession') === 'checkout-1') {
      return Response.json({ data: checkoutCreated ? [{ id: 'pay_1' }] : [], hasMore: false, totalCount: checkoutCreated ? 1 : 0 });
    }
    if (path === '/payments/pay_1') return Response.json({
      object: 'payment', id: 'pay_1', checkoutSession: 'checkout-1', subscription: 'sub_1',
      billingType: 'CREDIT_CARD', status: paymentStatus, value: 19.9,
      confirmedDate: '2026-09-01', deleted: false, refunds, chargeback,
    });
    if (path === '/subscriptions/sub_1' && init.method === 'DELETE') {
      subscriptionStatus = 'EXPIRED';
      return Response.json({ deleted: true });
    }
    if (path === '/subscriptions/sub_1') return Response.json({
      object: 'subscription', id: 'sub_1', billingType: 'CREDIT_CARD', cycle: 'MONTHLY',
      status: subscriptionStatus, value: 19.9, deleted: subscriptionStatus === 'EXPIRED',
    });
    if (path === '/checkouts/checkout-1/cancel' && init.method === 'POST') return Response.json({ id: 'checkout-1', status: 'CANCELED' });
    return new Response('', { status: 404 });
  };
  const options = { fetchImpl };
  return {
    sqlite, db, env, auth, calls, options,
    checkout: () => createAccountCheckout(env, auth, { offerKey: 'ralven_pro_monthly_1990' }, options),
    setPayment(status, nextRefunds = null) { paymentStatus = status; refunds = nextRefunds; },
    setChargeback(status) { chargeback = status ? { status } : null; },
  };
}

function webhook(env, id, event, resource, token = env.ASAAS_WEBHOOK_TOKEN) {
  return new Request('https://worker.test/billing/asaas/webhook', {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'asaas-access-token': token },
    body: JSON.stringify({ id, event, ...resource }),
  });
}

test('billing stays disabled without explicit Asaas environment, tokens and return URL', () => {
  assert.equal(billingConfig({}).enabled, false);
  assert.equal(billingConfig({ ASAAS_ACCESS_TOKEN: '$aact_prod_wrong-environment' }).providerReady, false);
  assert.equal(billingConfig({ ASAAS_ENVIRONMENT: 'sandbox', ASAAS_ACCESS_TOKEN: '$aact_prod_wrong-prefix',
    ASAAS_WEBHOOK_TOKEN: 'x'.repeat(32) }).providerReady, false);
  assert.equal(billingConfig({ ASAAS_ENVIRONMENT: 'sandbox', ASAAS_ACCESS_TOKEN: '$aact_hmlg_valid',
    ASAAS_WEBHOOK_TOKEN: 'short' }).providerReady, false);
});

test('checkout URL validation accepts only the exact Asaas hosted session URL', () => {
  assert.equal(validateCheckoutUrl('https://asaas.com/checkoutSession/show?id=checkout-1'),
    'https://asaas.com/checkoutSession/show?id=checkout-1');
  for (const value of [
    'http://asaas.com/checkoutSession/show?id=x',
    'https://asaas.com.evil.test/checkoutSession/show?id=x',
    'https://user@asaas.com/checkoutSession/show?id=x',
    'https://asaas.com/checkoutSession/show?id=x&id=y',
    'https://asaas.com/checkoutSession/show?id=x&next=evil',
    'https://asaas.com/other?id=x',
  ]) assert.equal(validateCheckoutUrl(value), null);
});

test('checkout uses the server offer, sends no account identity, and resumes one pending session', async t => {
  const f = setup(t);
  await assert.rejects(createAccountCheckout(f.env, f.auth, { offerKey: 'ralven_pro_monthly_1990', amountCents: 1 }, f.options), /invalid-offer/);
  const first = await f.checkout();
  assert.deepEqual(await f.checkout(), first);
  assert.equal(first.checkoutUrl, 'https://asaas.com/checkoutSession/show?id=checkout-1');
  const creates = f.calls.filter(call => call.method === 'POST' && call.url.endsWith('/v3/checkouts'));
  assert.equal(creates.length, 1);
  const payload = JSON.parse(creates[0].body);
  assert.equal(payload.items[0].value, 19.9);
  assert.deepEqual(payload.billingTypes, ['CREDIT_CARD']);
  assert.deepEqual(payload.chargeTypes, ['RECURRENT']);
  assert.equal(payload.subscription.cycle, 'MONTHLY');
  assert.doesNotMatch(JSON.stringify(payload), /buyer@example|user-1/);
  assert.match(creates[0].headers.access_token, /^\$aact_hmlg_/);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
});

test('an ambiguous checkout create never retries and risks a duplicate charge', async t => {
  const f = setup(t);
  await assert.rejects(createAccountCheckout(f.env, f.auth, { offerKey: 'ralven_pro_monthly_1990' }, {
    fetchImpl: async (url, init) => { await f.options.fetchImpl(url, init); throw new Error('timeout after commit'); },
  }), /provider-temporarily-unavailable/);
  await assert.rejects(f.checkout(), /billing-checkout-reconciliation-required/);
  assert.equal(f.calls.filter(call => call.method === 'POST').length, 1);
});

test('webhook authenticates before reading or fetching and accepts Asaas event IDs', async t => {
  const f = setup(t);
  const invalid = await handleAsaasWebhook(webhook(f.env, 'evt_invalid&1', 'PAYMENT_CONFIRMED',
    { payment: { id: 'pay_1' } }, 'wrong-token'), f.env, f.options);
  assert.equal(invalid.status, 401);
  assert.equal(f.calls.length, 0);
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_webhook_events').get().n, 0);
});

test('canonical confirmed payment grants Pro; duplicate event is idempotent', async t => {
  const f = setup(t); await f.checkout();
  const request = () => webhook(f.env, 'evt_confirmed&1', 'PAYMENT_CONFIRMED', { payment: { id: 'pay_1', value: 1 } });
  assert.equal((await handleAsaasWebhook(request(), f.env, f.options)).status, 200);
  const paid = await fetchAccountEntitlements(f.db, f.auth.uid);
  assert.equal(paid.tier, 'pro');
  const callCount = f.calls.length;
  assert.equal((await handleAsaasWebhook(request(), f.env, f.options)).status, 200);
  assert.equal(f.calls.length, callCount);
  assert.equal(f.sqlite.prepare('SELECT COUNT(*) AS n FROM billing_payments').get().n, 1);
});

test('partial completed refund and chargeback revoke access from canonical provider state', async t => {
  const f = setup(t); await f.checkout();
  await reconcilePayment(f.env, 'pay_1', 'evt_paid&1', f.options);
  f.setPayment('RECEIVED', [{ status: 'DONE', value: 1 }]);
  await reconcilePayment(f.env, 'pay_1', 'evt_refund&1', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
  assert.equal(f.sqlite.prepare("SELECT state FROM billing_payments WHERE provider_payment_id = 'pay_1'").get().state, 'refunded');
  f.setPayment('RECEIVED'); f.setChargeback('REQUESTED');
  await reconcilePayment(f.env, 'pay_1', 'evt_chargeback&1', f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'free');
});

test('refresh reconciles a missed payment webhook and cancellation stops the subscription', async t => {
  const f = setup(t); await f.checkout();
  await syncAccountBilling(f.env, f.auth, f.options);
  assert.equal((await fetchAccountEntitlements(f.db, f.auth.uid)).tier, 'pro');
  const result = await cancelAccountBilling(f.env, f.auth, f.options);
  assert.equal(result.subscription.state, 'cancelled');
  assert.ok(f.calls.some(call => call.method === 'DELETE' && call.url.endsWith('/v3/subscriptions/sub_1')));
  assert.equal(await deleteAccountProfile(f.db, f.auth.uid), true);
});

test('pending checkout cancellation calls the Asaas checkout endpoint', async t => {
  const f = setup(t); await f.checkout();
  const result = await cancelAccountBilling(f.env, f.auth, f.options);
  assert.equal(result.subscription.state, 'cancelled');
  assert.ok(f.calls.some(call => call.method === 'POST' && call.url.endsWith('/v3/checkouts/checkout-1/cancel')));
});

test('payment ownership and price mismatches cannot grant another account', async t => {
  const f = setup(t); await f.checkout();
  const options = { fetchImpl: async (url, init) => {
    const response = await f.options.fetchImpl(url, init);
    const body = await response.json();
    if (url.endsWith('/v3/payments/pay_1')) body.value = 1;
    return Response.json(body);
  } };
  await assert.rejects(reconcilePayment(f.env, 'pay_1', 'evt_bad&1', options), /billing-payment-mismatch/);
  assert.equal((await fetchAccountEntitlements(f.db, 'user-1')).tier, 'free');
  assert.equal((await fetchAccountEntitlements(f.db, 'user-2')).tier, 'free');
});

test('public return page never trusts query parameters or claims approval', async () => {
  const response = await worker.fetch(new Request(
    'https://worker.test/billing/return?status=CONFIRMED&id=secret&token=secret-token&message=%3Cscript%3Ealert(1)%3C/script%3E'), {});
  assert.equal(response.status, 200);
  assert.equal(response.headers.get('Cache-Control'), 'no-store');
  const html = await response.text();
  assert.match(html, /Esta página não confirma aprovação/);
  assert.match(html, /Asaas/);
  assert.doesNotMatch(html, /secret-token|<script|status=CONFIRMED|pagamento (aprovado|confirmado)/i);
});
