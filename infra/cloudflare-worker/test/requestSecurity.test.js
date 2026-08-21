import { test } from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';
import { readBoundedJson } from '../src/requestSecurity.js';

test('readBoundedJson parses a body within the byte limit', async () => {
  const request = new Request('https://worker.example/test', {
    method: 'POST',
    body: JSON.stringify({ value: 'ok' }),
  });

  assert.deepEqual(await readBoundedJson(request, 64), { value: 'ok' });
});

test('readBoundedJson rejects streamed bodies above the byte limit', async () => {
  const request = new Request('https://worker.example/test', {
    method: 'POST',
    body: JSON.stringify({ value: 'x'.repeat(128) }),
  });

  assert.equal(await readBoundedJson(request, 64), null);
});

test('readBoundedJson rejects a declared oversized body without reading it', async () => {
  const request = new Request('https://worker.example/test', {
    method: 'POST',
    headers: { 'Content-Length': '1000' },
    body: '{}',
  });

  assert.equal(await readBoundedJson(request, 64), null);
});

test('admin mutations reject requests outside the configured dashboard origin', async () => {
  for (const path of ['/admin/login', '/admin/logout', '/admin/live-alert']) {
    const response = await worker.fetch(new Request(`https://worker.example${path}`, {
      method: 'POST',
      headers: { Origin: 'https://evil.example' },
      body: path.endsWith('login') ? JSON.stringify({ password: 'irrelevant' }) : undefined,
    }), { DASHBOARD_ORIGIN: 'https://dashboard.example' });

    assert.equal(response.status, 403);
  }
});
