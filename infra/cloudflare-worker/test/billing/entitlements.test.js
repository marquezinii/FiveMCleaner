import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fetchAccountEntitlements } from '../../src/billing/entitlements.js';

const NOW = '2026-08-29T12:00:00.000Z';

function fakeDb(rows = []) {
  const calls = [];
  return {
    calls,
    prepare(sql) {
      return {
        bind(...params) {
          calls.push({ sql, params });
          return {
            async all() {
              const [uid, proKey, aiKey, validFromCutoff, validUntilCutoff] = params;
              return {
                results: rows.filter(candidate => candidate.account_uid === uid
                  && [proKey, aiKey].includes(candidate.entitlement_key)
                  && ['active', 'grace_period'].includes(candidate.state)
                  && candidate.valid_from <= validFromCutoff
                  && candidate.valid_until > validUntilCutoff)
                  .map(row => ({
                    entitlement_key: row.entitlement_key,
                    valid_from: row.valid_from,
                    valid_until: row.valid_until,
                  })),
              };
            },
          };
        },
      };
    },
  };
}

function entitlement(overrides = {}) {
  return {
    account_uid: 'firebase-uid-123',
    entitlement_key: 'ralven_pro',
    state: 'active',
    valid_from: '2026-08-01T00:00:00.000Z',
    valid_until: '2026-09-01T00:00:00.000Z',
    provider_subscription_id: 'must-never-leak',
    ...overrides,
  };
}

test('fetchAccountEntitlements grants current Pro and reports AI independently', async () => {
  for (const state of ['active', 'grace_period']) {
    const db = fakeDb([
      entitlement({ state }),
      entitlement({ state, entitlement_key: 'ralven_ai' }),
    ]);
    assert.deepEqual(await fetchAccountEntitlements(db, 'firebase-uid-123', NOW), {
      tier: 'pro',
      entitlements: ['ralven_pro', 'ralven_ai'],
      validFrom: '2026-08-01T00:00:00.000Z',
      validUntil: '2026-09-01T00:00:00.000Z',
      aiValidFrom: '2026-08-01T00:00:00.000Z',
      aiValidUntil: '2026-09-01T00:00:00.000Z',
    });

    const [{ sql, params }] = db.calls;
    assert.match(sql, /entitlement_key IN \(\?, \?\)/);
    assert.match(sql, /state IN \('active', 'grace_period'\)/);
    assert.match(sql, /valid_from <= \?/);
    assert.match(sql, /valid_until > \?/);
    assert.doesNotMatch(sql, /provider|subscription/i);
    assert.deepEqual(params, ['firebase-uid-123', 'ralven_pro', 'ralven_ai', NOW, NOW]);
  }
});

test('fetchAccountEntitlements does not infer AI access from Pro', async () => {
  assert.deepEqual(await fetchAccountEntitlements(
    fakeDb([entitlement()]),
    'firebase-uid-123',
    NOW,
  ), {
    tier: 'pro',
    entitlements: ['ralven_pro'],
    validFrom: '2026-08-01T00:00:00.000Z',
    validUntil: '2026-09-01T00:00:00.000Z',
    aiValidFrom: null,
    aiValidUntil: null,
  });
});

test('fetchAccountEntitlements returns free for absent, orphaned AI, or invalid Pro access', async () => {
  const cases = [
    [],
    [entitlement({ entitlement_key: 'ralven_ai' })],
    [entitlement({ state: 'revoked' })],
    [entitlement({ state: 'expired' })],
    [entitlement({ valid_until: NOW })],
    [entitlement({ valid_from: '2026-08-29T12:00:00.001Z' })],
    [entitlement({ entitlement_key: 'another_product' })],
    [entitlement({ account_uid: 'another-user' })],
  ];

  for (const rows of cases) {
    assert.deepEqual(await fetchAccountEntitlements(fakeDb(rows), 'firebase-uid-123', NOW), {
      tier: 'free',
      entitlements: [],
      validFrom: null,
      validUntil: null,
      aiValidFrom: null,
      aiValidUntil: null,
    });
  }
});

test('fetchAccountEntitlements exposes no provider data', async () => {
  const result = await fetchAccountEntitlements(
    fakeDb([entitlement({ valid_from: NOW })]),
    'firebase-uid-123',
    NOW,
  );

  assert.deepEqual(Object.keys(result).sort(), [
    'aiValidFrom', 'aiValidUntil', 'entitlements', 'tier', 'validFrom', 'validUntil',
  ]);
  assert.equal(JSON.stringify(result).includes('must-never-leak'), false);
});
