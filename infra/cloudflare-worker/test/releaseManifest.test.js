import test from 'node:test';
import assert from 'node:assert/strict';
import worker from '../src/index.js';

test('signed release feed is unavailable by default and no-store when configured', async () => {
  const unavailable = await worker.fetch(
    new Request('https://worker.example/update/manifest'),
    { DASHBOARD_ORIGIN: 'https://dashboard.example' },
  );
  assert.equal(unavailable.status, 503);

  const manifest = JSON.stringify({
    channel: 'stable',
    version: '2.0.0',
    minimumAllowedVersion: '1.1.3',
    packageUrl: 'https://github.com/marquezinii/FiveMCleaner/releases/download/v2.0.0/FiveMCleaner-Runtime-win-x64.zip',
    packageSha256: 'a'.repeat(64),
    packageSizeBytes: 1024,
    signatureBase64: 'a'.repeat(96),
  });
  const available = await worker.fetch(
    new Request('https://worker.example/update/manifest'),
    { DASHBOARD_ORIGIN: 'https://dashboard.example', RELEASE_MANIFEST_JSON: manifest },
  );
  assert.equal(available.status, 200);
  assert.equal(available.headers.get('cache-control'), 'no-store');
  assert.deepEqual(await available.json(), JSON.parse(manifest));
});
