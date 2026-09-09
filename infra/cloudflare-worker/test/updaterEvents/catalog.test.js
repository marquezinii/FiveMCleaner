import test from 'node:test';
import assert from 'node:assert/strict';
import { describeUpdaterEventCode } from '../../src/updaterEvents/catalog.js';

test('updater catalog exposes a compact identifier and actionable name', () => {
  assert.deepEqual(describeUpdaterEventCode('u101'), {
    error_id: 'U101',
    error_name: 'Manifesto: origem ou resposta recusada',
    error_group: 'Manifesto',
  });
});

test('updater catalog keeps historical codes understandable', () => {
  assert.deepEqual(describeUpdaterEventCode('security-policy'), {
    error_id: 'SECURITY-POLICY',
    error_name: 'Cliente anterior: política de segurança recusou',
    error_group: 'Legado',
  });
});
