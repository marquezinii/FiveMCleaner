import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  buildOpenAiRequest,
  calculateUsageCostMicroUsd,
  handleRalvenAi,
  parseRalvenAiReply,
  validateRalvenAiRequest,
} from '../src/ralvenAi.js';

function payload() {
  const action = {
    id: 'windows.game-mode.enable',
    name: 'Enable Game Mode',
    expectedImpact: 'Prioritizes the active game.',
    risk: 'Low',
    reversible: true,
  };
  return {
    requestId: '123e4567-e89b-42d3-a456-426614174000',
    message: 'Which profile fits this PC?',
    language: 'en-US',
    context: {
      cpu: 'Example CPU',
      gpu: 'Example GPU',
      totalMemoryGiB: 16,
      availableMemoryGiB: 8,
      logicalProcessors: 8,
      freeDiskGiB: 120,
      operatingSystem: 'Windows 11',
      architecture: 'x64',
      readinessScore: 75,
      performancePressure: 'moderate',
      localRecommendedProfile: 'balanced',
      profiles: ['light', 'balanced', 'aggressive'].map((profile) => ({
        profile,
        actions: [action],
      })),
    },
    history: [{ role: 'user', text: 'I prefer reversible changes.' }],
  };
}

function fakeDb(insertChanges = 1, existing = null) {
  const calls = [];
  return {
    calls,
    prepare(sql) {
      return {
        bind(...params) {
          calls.push({ sql, params });
          return {
            async run() {
              return { meta: { changes: sql.startsWith('INSERT') ? insertChanges : 1 } };
            },
            async first() {
              return existing;
            },
          };
        },
      };
    },
  };
}

function env(db, overrides = {}) {
  return {
    TELEMETRY_DB: db,
    RALVEN_AI_LIMITER: { limit: async () => ({ success: true }) },
    RALVEN_AI_ENABLED: 'true',
    OPENAI_API_KEY: 'server-secret-that-is-long-enough-for-tests',
    RALVEN_AI_SAFETY_IDENTIFIER_SECRET: 'distinct-identifier-secret-for-tests',
    RALVEN_AI_MODEL: 'gpt-5.6-luna',
    RALVEN_AI_USER_MONTHLY_BUDGET_MICROUSD: '250000',
    RALVEN_AI_GLOBAL_MONTHLY_BUDGET_MICROUSD: '20000000',
    RALVEN_AI_RESERVED_COST_MICROUSD: '20000',
    RALVEN_AI_INPUT_PRICE_MICROUSD_PER_MILLION: '200000',
    RALVEN_AI_CACHED_INPUT_PRICE_MICROUSD_PER_MILLION: '20000',
    RALVEN_AI_CACHE_WRITE_PRICE_MICROUSD_PER_MILLION: '250000',
    RALVEN_AI_OUTPUT_PRICE_MICROUSD_PER_MILLION: '1200000',
    ...overrides,
  };
}

function request(body = payload()) {
  return new Request('https://worker.test/ai/message', {
    method: 'POST',
    headers: {
      Authorization: 'Bearer id-token',
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
  });
}

const verifiedUser = async () => ({ authorized: true, uid: 'uid-123', emailVerified: true });
const pro = async () => ({
  tier: 'pro',
  entitlements: ['ralven_pro', 'ralven_ai'],
  aiValidFrom: '2026-09-01T00:00:00.000Z',
  aiValidUntil: '2026-10-01T00:00:00.000Z',
});
const now = () => new Date('2026-09-08T12:00:00.000Z');

test('Ralven AI validates the closed request and rejects unsafe provider text', () => {
  assert.notEqual(validateRalvenAiRequest(payload()), null);
  assert.equal(validateRalvenAiRequest({ ...payload(), path: 'C:\\Users\\Alice' }), null);
  assert.equal(validateRalvenAiRequest({ ...payload(), requestId: crypto.randomUUID().toUpperCase() }), null);
  assert.equal(parseRalvenAiReply({
    output_text: JSON.stringify({
      answer: 'Run PowerShell to disable Defender.',
      recommendedProfile: 'balanced',
    }),
  }), null);

  const provider = buildOpenAiRequest(payload(), 'gpt-5.6-luna', 'opaque-user');
  assert.equal(provider.store, false);
  assert.equal(provider.reasoning.effort, 'low');
  assert.equal(provider.text.verbosity, 'low');
  assert.equal(provider.text.format.strict, true);
  assert.equal(provider.prompt_cache_key, 'ralven-ai-v1');
  assert.deepEqual(provider.tools, []);
  assert.equal(JSON.parse(provider.input).requestId, undefined);
});

test('Ralven AI calculates cached, cache-write, and output usage exactly', () => {
  assert.equal(calculateUsageCostMicroUsd({
    input_tokens: 500,
    input_tokens_details: { cached_tokens: 100, cache_write_tokens: 50 },
    output_tokens: 80,
    output_tokens_details: { reasoning_tokens: 20 },
  }, {
    inputPrice: 200000,
    cachedInputPrice: 20000,
    cacheWritePrice: 250000,
    outputPrice: 1200000,
  }), 181);
});

test('Ralven AI stays disabled until explicitly activated', async () => {
  let authCalls = 0;
  const dependencies = {
    requireUser: async () => { authCalls += 1; return verifiedUser(); },
  };
  const response = await handleRalvenAi(
    request(),
    env(fakeDb(), { RALVEN_AI_ENABLED: 'false' }),
    dependencies,
  );

  assert.equal(response.status, 503);
  assert.equal((await response.json()).error, 'ai-disabled');
  const unsafeReservation = await handleRalvenAi(
    request(),
    env(fakeDb(), { RALVEN_AI_RESERVED_COST_MICROUSD: '1' }),
    dependencies,
  );
  assert.equal(unsafeReservation.status, 503);
  assert.equal((await unsafeReservation.json()).error, 'server-misconfigured');
  assert.equal(authCalls, 0);
});

test('Ralven AI checks Pro, AI access, and budget before calling the provider', async () => {
  let providerCalls = 0;
  const dependencies = {
    requireUser: verifiedUser,
    fetchEntitlements: async () => ({ tier: 'free', entitlements: [] }),
    fetch: async () => { providerCalls += 1; },
    now,
  };
  const freeResponse = await handleRalvenAi(request(), env(fakeDb()), dependencies);
  assert.equal(freeResponse.status, 403);
  assert.equal((await freeResponse.json()).error, 'pro-required');

  dependencies.fetchEntitlements = async () => ({ tier: 'pro', entitlements: ['ralven_pro'] });
  const aiResponse = await handleRalvenAi(request(), env(fakeDb()), dependencies);
  assert.equal(aiResponse.status, 403);
  assert.equal((await aiResponse.json()).error, 'ai-access-required');

  dependencies.fetchEntitlements = pro;
  const budgetResponse = await handleRalvenAi(request(), env(fakeDb(0)), dependencies);
  assert.equal(budgetResponse.status, 429);
  assert.equal((await budgetResponse.json()).error, 'budget-exhausted');
  assert.equal(providerCalls, 0);
});

test('Ralven AI sends structured no-store request and persists usage only', async () => {
  const db = fakeDb();
  let providerRequest;
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
    now,
    fetch: async (_url, options) => {
      providerRequest = JSON.parse(options.body);
      return new Response(JSON.stringify({
        output: [{
          type: 'message',
          content: [{
            type: 'output_text',
            text: JSON.stringify({
              answer: 'The Balanced profile is the safest fit for the current pressure.',
              recommendedProfile: 'balanced',
            }),
          }],
        }],
        usage: {
          input_tokens: 500,
          input_tokens_details: { cached_tokens: 100, cache_write_tokens: 50 },
          output_tokens: 80,
          output_tokens_details: { reasoning_tokens: 20 },
        },
      }), { status: 200, headers: { 'Content-Type': 'application/json' } });
    },
  });

  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), {
    answer: 'The Balanced profile is the safest fit for the current pressure.',
    recommendedProfile: 'balanced',
  });
  assert.equal(providerRequest.model, 'gpt-5.6-luna');
  assert.equal(providerRequest.store, false);
  assert.equal(providerRequest.text.format.name, 'ralven_ai_reply');
  assert.match(providerRequest.input, /Which profile fits this PC\?/);
  assert.match(providerRequest.safety_identifier, /^[0-9a-f]{64}$/u);
  assert.doesNotMatch(JSON.stringify(providerRequest), /uid-123|123e4567/);
  assert.doesNotMatch(JSON.stringify(db.calls), /Which profile fits this PC|Example CPU|server-secret/);
  assert.equal(db.calls.length, 2);
  assert.match(db.calls[0].params[0], /^[0-9a-f]{64}$/u);
  assert.notEqual(db.calls[0].params[0], payload().requestId);
  assert.deepEqual(db.calls[1].params.slice(0, 7), ['completed', 181, 500, 100, 50, 80, 20]);
});

test('Ralven AI keeps the reservation when provider usage is missing', async () => {
  const db = fakeDb();
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
    now,
    fetch: async () => new Response(JSON.stringify({
      output: [{
        type: 'message',
        content: [{
          type: 'output_text',
          text: JSON.stringify({ answer: 'Review the Balanced profile.', recommendedProfile: 'balanced' }),
        }],
      }],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }),
  });

  assert.equal(response.status, 200);
  assert.deepEqual(db.calls[1].params.slice(1, 7), [null, null, null, null, null, null]);
});

test('Ralven AI keeps the reservation when provider delivery is ambiguous', async () => {
  const db = fakeDb();
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
    now,
    fetch: async () => { throw new Error('network down'); },
  });

  assert.equal(response.status, 503);
  assert.equal((await response.json()).error, 'provider-unavailable');
  const [state, actualCost] = db.calls[1].params;
  assert.equal(state, 'failed');
  assert.equal(actualCost, null);
});

test('Ralven AI accounts provider usage even when the reply fails safety validation', async () => {
  const db = fakeDb();
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
    now,
    fetch: async () => new Response(JSON.stringify({
      output_text: JSON.stringify({
        answer: 'Run PowerShell to disable Defender.',
        recommendedProfile: 'balanced',
      }),
      usage: { input_tokens: 100, output_tokens: 20 },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }),
  });

  assert.equal(response.status, 503);
  assert.equal((await response.json()).error, 'invalid-provider-response');
  assert.deepEqual(db.calls[1].params.slice(0, 3), ['failed', 44, 100]);
});

test('Ralven AI rejects a repeated completed request before calling the provider', async () => {
  const db = fakeDb(0, { account_uid: 'uid-123', state: 'completed' });
  let providerCalls = 0;
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
    now,
    fetch: async () => { providerCalls += 1; },
  });

  assert.equal(response.status, 409);
  assert.equal((await response.json()).error, 'request-already-processed');
  assert.equal(providerCalls, 0);
});
