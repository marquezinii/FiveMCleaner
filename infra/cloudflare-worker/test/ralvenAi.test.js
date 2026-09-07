import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  buildOpenAiRequest,
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

function fakeDb(insertChanges = 1) {
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
          };
        },
      };
    },
  };
}

function env(db) {
  return {
    TELEMETRY_DB: db,
    RALVEN_AI_LIMITER: { limit: async () => ({ success: true }) },
    OPENAI_API_KEY: 'server-secret',
    RALVEN_AI_MODEL: 'gpt-5.6-luna',
    RALVEN_AI_USER_MONTHLY_BUDGET_MICROUSD: '350000',
    RALVEN_AI_GLOBAL_MONTHLY_BUDGET_MICROUSD: '20000000',
    RALVEN_AI_RESERVED_COST_MICROUSD: '6000',
    RALVEN_AI_INPUT_PRICE_MICROUSD_PER_MILLION: '200000',
    RALVEN_AI_OUTPUT_PRICE_MICROUSD_PER_MILLION: '1200000',
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
const pro = async () => ({ tier: 'pro', entitlements: ['ralven_pro'] });

test('Ralven AI validates the closed request and rejects unsafe provider text', () => {
  assert.notEqual(validateRalvenAiRequest(payload()), null);
  assert.equal(validateRalvenAiRequest({ ...payload(), path: 'C:\\Users\\Alice' }), null);
  assert.equal(parseRalvenAiReply({
    output_text: JSON.stringify({
      answer: 'Run PowerShell to disable Defender.',
      recommendedProfile: 'balanced',
    }),
  }), null);

  const provider = buildOpenAiRequest(payload(), 'gpt-5.6-luna', 'opaque-user');
  assert.equal(provider.store, false);
  assert.equal(provider.reasoning.effort, 'low');
  assert.equal(provider.text.format.strict, true);
  assert.equal(provider.tools, undefined);
});

test('Ralven AI checks Pro and monthly budget before calling the provider', async () => {
  let providerCalls = 0;
  const dependencies = {
    requireUser: verifiedUser,
    fetchEntitlements: async () => ({ tier: 'free', entitlements: [] }),
    fetch: async () => { providerCalls += 1; },
  };
  const freeResponse = await handleRalvenAi(request(), env(fakeDb()), dependencies);
  assert.equal(freeResponse.status, 403);

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
        usage: { input_tokens: 500, output_tokens: 80 },
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
  assert.doesNotMatch(JSON.stringify(db.calls), /Which profile fits this PC|Example CPU|server-secret/);
  assert.equal(db.calls.length, 2);
});

test('Ralven AI keeps the reservation when provider usage is missing', async () => {
  const db = fakeDb();
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
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
  assert.deepEqual(db.calls[1].params.slice(1, 4), [null, null, null]);
});

test('Ralven AI does not permanently burn the budget on a provider failure', async () => {
  const db = fakeDb();
  const response = await handleRalvenAi(request(), env(db), {
    requireUser: verifiedUser,
    fetchEntitlements: pro,
    fetch: async () => { throw new Error('network down'); },
  });

  assert.equal(response.status, 503);
  assert.equal((await response.json()).error, 'provider-unavailable');
  const [state, actualCost] = db.calls[1].params;
  assert.equal(state, 'failed');
  assert.equal(actualCost, 0);
});
