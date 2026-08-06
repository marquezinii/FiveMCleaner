import { test } from 'node:test';
import assert from 'node:assert/strict';
import { chunkStatements, MAX_D1_BATCH_STATEMENTS } from '../src/index.js';

test('chunkStatements returns a single chunk for an empty list', () => {
  assert.deepEqual(chunkStatements([]), []);
});

test('chunkStatements keeps a small batch in one chunk', () => {
  const statements = [1, 2, 3];
  assert.deepEqual(chunkStatements(statements), [[1, 2, 3]]);
});

test('chunkStatements splits at exactly the D1 500-statement limit', () => {
  const statements = Array.from({ length: MAX_D1_BATCH_STATEMENTS }, (_, i) => i);
  assert.deepEqual(chunkStatements(statements), [statements]);
});

test('chunkStatements splits past the limit into bounded chunks preserving order', () => {
  const statements = Array.from({ length: 1500 }, (_, i) => i);
  const chunks = chunkStatements(statements);

  assert.equal(chunks.length, 3);
  for (const chunk of chunks) {
    assert.ok(chunk.length <= MAX_D1_BATCH_STATEMENTS);
  }

  assert.deepEqual(chunks.flat(), statements);
});
