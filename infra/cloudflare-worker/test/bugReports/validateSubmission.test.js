import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateBugReport } from '../../src/bugReports/validateSubmission.js';

function validSubmission(overrides = {}) {
  return {
    reportId: '11111111-1111-1111-1111-111111111111',
    category: 'Falha na otimização',
    summary: 'O preset não terminou',
    description: 'Ao aplicar o perfil médio, a operação parou antes da conclusão.',
    appVersion: '1.0.4',
    profile: 'Médio',
    technicalSummary: 'Windows 11; perfil médio',
    email: null,
    logText: null,
    environment: 'Production',
    ...overrides,
  };
}

test('validateBugReport accepts a well-formed submission with no email/log', () => {
  const result = validateBugReport(validSubmission());

  assert.ok(result);
  assert.equal(result.category, 'Falha na otimização');
  assert.equal(result.email, null);
  assert.equal(result.logText, null);
});

test('validateBugReport accepts a submission with a valid email and log excerpt', () => {
  const result = validateBugReport(validSubmission({ email: 'user@example.com', logText: 'crash log excerpt' }));

  assert.ok(result);
  assert.equal(result.email, 'user@example.com');
  assert.equal(result.logText, 'crash log excerpt');
});

test('validateBugReport trims summary and description', () => {
  const result = validateBugReport(validSubmission({ summary: '  hello there  ', description: '  ' + 'x'.repeat(25) + '  ' }));

  assert.equal(result.summary, 'hello there');
  assert.ok(!result.description.startsWith(' '));
});

test('validateBugReport rejects a missing reportId', () => {
  assert.equal(validateBugReport(validSubmission({ reportId: '' })), null);
  assert.equal(validateBugReport(validSubmission({ reportId: undefined })), null);
});

test('validateBugReport rejects a category with a newline', () => {
  assert.equal(validateBugReport(validSubmission({ category: 'a\nb' })), null);
});

test('validateBugReport rejects a summary that is too short or too long', () => {
  assert.equal(validateBugReport(validSubmission({ summary: 'hi' })), null);
  assert.equal(validateBugReport(validSubmission({ summary: 'x'.repeat(121) })), null);
});

test('validateBugReport rejects a description that is too short or too long', () => {
  assert.equal(validateBugReport(validSubmission({ description: 'short' })), null);
  assert.equal(validateBugReport(validSubmission({ description: 'x'.repeat(8001) })), null);
});

test('validateBugReport rejects an invalid app version', () => {
  assert.equal(validateBugReport(validSubmission({ appVersion: '1.0.4; DROP TABLE' })), null);
  assert.equal(validateBugReport(validSubmission({ appVersion: '' })), null);
});

test('validateBugReport rejects an unknown environment', () => {
  assert.equal(validateBugReport(validSubmission({ environment: 'Staging' })), null);
});

test('validateBugReport rejects a technical summary over the limit', () => {
  assert.equal(validateBugReport(validSubmission({ technicalSummary: 'x'.repeat(513) })), null);
});

test('validateBugReport rejects a malformed email', () => {
  assert.equal(validateBugReport(validSubmission({ email: 'not-an-email' })), null);
  assert.equal(validateBugReport(validSubmission({ email: 'x'.repeat(255) + '@example.com' })), null);
});

test('validateBugReport accepts an empty-string email/log as "not provided"', () => {
  const result = validateBugReport(validSubmission({ email: '', logText: '' }));

  assert.ok(result);
  assert.equal(result.email, null);
  assert.equal(result.logText, null);
});

test('validateBugReport rejects a log excerpt over the 100 KB limit', () => {
  assert.equal(validateBugReport(validSubmission({ logText: 'a'.repeat(101 * 1024) })), null);
});

test('validateBugReport rejects a payload that is not an object', () => {
  assert.equal(validateBugReport(null), null);
  assert.equal(validateBugReport('nope'), null);
  assert.equal(validateBugReport(42), null);
});
