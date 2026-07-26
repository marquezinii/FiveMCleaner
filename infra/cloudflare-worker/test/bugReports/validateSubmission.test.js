import { test } from 'node:test';
import assert from 'node:assert/strict';
import { validateBugReport } from '../../src/bugReports/validateSubmission.js';

const PNG_HEADER_BASE64 = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0]).toString('base64');

function validSubmission(overrides = {}) {
  return {
    reportId: '11111111-1111-1111-1111-111111111111',
    category: 'Falha na otimização',
    summary: 'O preset não terminou',
    description: 'Ao aplicar o perfil médio, a operação parou antes da conclusão.',
    appVersion: '1.0.4',
    profile: 'Médio',
    technicalSummary: 'Windows 11; perfil médio',
    environment: 'Production',
    attachment: null,
    ...overrides,
  };
}

test('validateBugReport accepts a well-formed submission without an attachment', () => {
  const result = validateBugReport(validSubmission());

  assert.ok(result);
  assert.equal(result.category, 'Falha na otimização');
  assert.equal(result.attachment, null);
});

test('validateBugReport accepts a submission with a valid PNG attachment', () => {
  const result = validateBugReport(
    validSubmission({
      attachment: { fileName: 'captura-test.png', contentType: 'image/png', contentBase64: PNG_HEADER_BASE64 },
    }),
  );

  assert.ok(result);
  assert.ok(result.attachment);
  assert.equal(result.attachment.fileName, 'captura-test.png');
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

test('validateBugReport rejects an attachment with the wrong content type', () => {
  assert.equal(
    validateBugReport(
      validSubmission({
        attachment: { fileName: 'captura-test.png', contentType: 'image/jpeg', contentBase64: PNG_HEADER_BASE64 },
      }),
    ),
    null,
  );
});

test('validateBugReport rejects an attachment whose filename does not match the sanitized pattern', () => {
  assert.equal(
    validateBugReport(
      validSubmission({
        attachment: { fileName: '../../etc/passwd.png', contentType: 'image/png', contentBase64: PNG_HEADER_BASE64 },
      }),
    ),
    null,
  );
  assert.equal(
    validateBugReport(
      validSubmission({
        attachment: { fileName: 'screenshot.png', contentType: 'image/png', contentBase64: PNG_HEADER_BASE64 },
      }),
    ),
    null,
  );
});

test('validateBugReport rejects an attachment whose bytes do not start with the PNG magic number', () => {
  const notPng = Buffer.from('not a real png here').toString('base64');

  assert.equal(
    validateBugReport(
      validSubmission({
        attachment: { fileName: 'captura-test.png', contentType: 'image/png', contentBase64: notPng },
      }),
    ),
    null,
  );
});

test('validateBugReport rejects a payload that is not an object', () => {
  assert.equal(validateBugReport(null), null);
  assert.equal(validateBugReport('nope'), null);
  assert.equal(validateBugReport(42), null);
});
