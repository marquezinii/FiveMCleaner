import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildStatsUrl, buildCsvUrl, buildBugsUrl, buildUpdaterEventsUrl, requestJson } from '../assets/api.js';

const BASE = 'https://telemetry.example.workers.dev';

test('buildStatsUrl builds the plain JSON endpoint with no filters', () => {
  assert.equal(buildStatsUrl(BASE, 'runs-per-day'), `${BASE}/api/stats/runs-per-day`);
});

test('buildStatsUrl applies from/to/version/environment as query params', () => {
  const url = buildStatsUrl(BASE, 'runs-per-day', {
    from: '2026-01-01',
    to: '2026-01-31',
    version: '1.0.4',
    environment: 'Production',
  });

  const parsed = new URL(url);
  assert.equal(parsed.searchParams.get('from'), '2026-01-01');
  assert.equal(parsed.searchParams.get('to'), '2026-01-31');
  assert.equal(parsed.searchParams.get('version'), '1.0.4');
  assert.equal(parsed.searchParams.get('environment'), 'Production');
});

test('buildStatsUrl omits filters that were not provided', () => {
  const url = new URL(buildStatsUrl(BASE, 'runs-per-day', { version: '1.0.4' }));

  assert.equal(url.searchParams.has('from'), false);
  assert.equal(url.searchParams.get('version'), '1.0.4');
});

test('buildCsvUrl appends .csv to the stat name and keeps the filters', () => {
  const url = new URL(buildCsvUrl(BASE, 'runs-per-day', { version: '1.0.4' }));

  assert.equal(url.pathname, '/api/stats/runs-per-day.csv');
  assert.equal(url.searchParams.get('version'), '1.0.4');
});

test('requestJson returns unauthorized:true on a 401 without throwing', async () => {
  const fakeFetch = async () => new Response(null, { status: 401 });

  const result = await requestJson('https://example.com', {}, fakeFetch);

  assert.deepEqual(result, { unauthorized: true });
});

test('requestJson returns an error marker on any other non-OK status', async () => {
  const fakeFetch = async () => new Response(null, { status: 500 });

  const result = await requestJson('https://example.com', {}, fakeFetch);

  assert.equal(result.error, 'request-failed-500');
});

test('requestJson returns the parsed JSON body on success', async () => {
  const fakeFetch = async () =>
    new Response(JSON.stringify([{ day: '2026-01-01', runs: 5 }]), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    });

  const result = await requestJson('https://example.com', {}, fakeFetch);

  assert.deepEqual(result.data, [{ day: '2026-01-01', runs: 5 }]);
});

test('requestJson always sends credentials so the session cookie is included', async () => {
  let capturedOptions;
  const fakeFetch = async (_url, options) => {
    capturedOptions = options;
    return new Response('{}', { status: 200 });
  };

  await requestJson('https://example.com', { method: 'GET' }, fakeFetch);

  assert.equal(capturedOptions.credentials, 'include');
});

test('buildBugsUrl builds the plain endpoint with no filters', () => {
  assert.equal(buildBugsUrl(BASE), `${BASE}/api/bugs`);
});

test('buildBugsUrl applies environment and category filters', () => {
  const url = new URL(buildBugsUrl(BASE, { environment: 'Production', category: 'Crash' }));

  assert.equal(url.searchParams.get('environment'), 'Production');
  assert.equal(url.searchParams.get('category'), 'Crash');
});

test('buildUpdaterEventsUrl applies version and environment filters', () => {
  const url = new URL(buildUpdaterEventsUrl(BASE, { version: '1.2.0', environment: 'Production' }));
  assert.equal(url.pathname, '/api/updater-events');
  assert.equal(url.searchParams.get('version'), '1.2.0');
});
