import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildLiveAlertUpsert, toLiveAlertResponse } from '../../src/liveAlert/store.js';

test('buildLiveAlertUpsert updates message, active and updated_at when message is provided', () => {
  const query = buildLiveAlertUpsert({ message: 'oi', active: true }, '2026-08-17T12:00:00.000Z');
  assert.equal(query.sql, 'UPDATE live_alert SET message = ?, active = ?, updated_at = ? WHERE id = 1');
  assert.deepEqual(query.params, ['oi', 1, '2026-08-17T12:00:00.000Z']);
});

test('buildLiveAlertUpsert stores a selected severity with the broadcast', () => {
  const query = buildLiveAlertUpsert({ message: 'oi', active: true, severity: 'critical' }, '2026-08-17T12:00:00.000Z');
  assert.equal(query.sql, 'UPDATE live_alert SET message = ?, active = ?, severity = ?, updated_at = ? WHERE id = 1');
  assert.deepEqual(query.params, ['oi', 1, 'critical', '2026-08-17T12:00:00.000Z']);
});

test('buildLiveAlertUpsert only touches active and updated_at when message is omitted', () => {
  const query = buildLiveAlertUpsert({ active: false }, '2026-08-17T12:00:00.000Z');
  assert.equal(query.sql, 'UPDATE live_alert SET active = ?, updated_at = ? WHERE id = 1');
  assert.deepEqual(query.params, [0, '2026-08-17T12:00:00.000Z']);
});

test('toLiveAlertResponse maps a D1 row to the public response shape', () => {
  const response = toLiveAlertResponse({ message: 'oi', active: 1, severity: 'critical', updated_at: '2026-08-17T12:00:00.000Z' });
  assert.deepEqual(response, { id: '2026-08-17T12:00:00.000Z', message: 'oi', active: true, severity: 'critical' });
});

test('toLiveAlertResponse treats a missing row as inactive', () => {
  assert.deepEqual(toLiveAlertResponse(null), { id: null, message: '', active: false, severity: 'important' });
});

test('toLiveAlertResponse treats active:0 as false', () => {
  const response = toLiveAlertResponse({ message: 'aviso antigo', active: 0, severity: 'info', updated_at: '2026-08-17T12:00:00.000Z' });
  assert.deepEqual(response, { id: '2026-08-17T12:00:00.000Z', message: '', active: false, severity: 'info' });
});
