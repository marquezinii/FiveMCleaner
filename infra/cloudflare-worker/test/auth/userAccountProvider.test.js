import test from 'node:test';
import assert from 'node:assert/strict';
import { CURRENT_TERMS_VERSION, createUserAccountProvider, hasStrongPassword, normalizeRegistration } from '../../src/auth/userAccountProvider.js';

const validRegistration = {
  firstName: ' João ',
  lastName: ' da Silva ',
  username: ' Joao.Silva ',
  email: ' JOAO@EXAMPLE.COM ',
  password: 'SenhaSegura!123',
  termsAccepted: true,
  termsVersion: CURRENT_TERMS_VERSION,
};

test('normalizeRegistration requires mandatory fields and permits an empty optional last name', () => {
  assert.deepEqual(normalizeRegistration(validRegistration), {
    firstName: 'João',
    lastName: 'da Silva',
    username: 'joao.silva',
    email: 'joao@example.com',
    password: 'SenhaSegura!123',
  });

  for (const field of ['firstName', 'username', 'email', 'password']) {
    assert.equal(normalizeRegistration({ ...validRegistration, [field]: '' }), null, field);
  }
  assert.deepEqual(normalizeRegistration({ ...validRegistration, lastName: '' }), { ...normalizeRegistration(validRegistration), lastName: '' });
  assert.equal(normalizeRegistration({ ...validRegistration, termsAccepted: false }), null);
  assert.equal(normalizeRegistration({ ...validRegistration, termsVersion: 'outdated' }), null);
});

test('normalizeRegistration rejects unsafe names, usernames and incomplete password requirements', () => {
  assert.equal(normalizeRegistration({ ...validRegistration, firstName: 'João123' }), null);
  assert.equal(normalizeRegistration({ ...validRegistration, username: '.invalid' }), null);
  assert.equal(normalizeRegistration({ ...validRegistration, username: 'ab' }), null);
  assert.equal(normalizeRegistration({ ...validRegistration, password: 'SenhaSegura123' }), null);
  assert.equal(normalizeRegistration({ ...validRegistration, password: 'senhasegura!123' }), null);
  assert.equal(normalizeRegistration({ ...validRegistration, password: 'SENHASEGURA!123' }), null);
  assert.equal(hasStrongPassword('SenhaSegura!123'), true);
});

test('register stores username and versioned terms acceptance before creating a session', async () => {
  const statements = [];
  const preparedSql = [];
  const db = {
    prepare(sql) {
      preparedSql.push(sql);
      const statement = {
        bind(...values) {
          statement.values = values;
          return statement;
        },
        async first() { return sql.includes('INSERT INTO user_registration_attempts') ? { account_count: 1 } : null; },
        async run() {
          statements.push({ sql, values: statement.values });
          return { success: true };
        },
      };
      return statement;
    },
  };
  const now = new Date('2026-08-02T12:00:00.000Z');
  const provider = createUserAccountProvider({ TELEMETRY_DB: db, IP_HASH_SECRET: 'test-secret' }, () => now);
  const response = await provider.register(new Request('https://example.test/account/register', {
    method: 'POST',
    body: JSON.stringify(validRegistration),
    headers: { 'Content-Type': 'application/json' },
  }));
  const payload = await response.json();

  assert.equal(response.status, 201);
  assert.equal(payload.profile.username, 'joao.silva');
  assert.equal(statements.length, 2);
  assert.ok(preparedSql.some(sql => sql.includes('INSERT INTO user_registration_attempts')));
  assert.match(statements[0].sql, /username_normalized/);
  assert.deepEqual(statements[0].values.slice(1, 5), ['João', 'da Silva', 'joao.silva', 'joao@example.com']);
  assert.equal(statements[0].values.at(-2), CURRENT_TERMS_VERSION);
  assert.equal(statements[0].values.at(-1), now.toISOString());
  assert.match(statements[1].sql, /INSERT INTO user_sessions/);
});

test('register limits expensive account creation by HMACd client IP', async () => {
  const db = {
    prepare(sql) {
      const statement = {
        bind() { return statement; },
        async first() { return null; },
        async run() { throw new Error('no write expected while rate limited'); },
      };
      return statement;
    },
  };
  const provider = createUserAccountProvider(
    { TELEMETRY_DB: db, IP_HASH_SECRET: 'test-secret' },
    () => new Date('2026-08-02T12:00:00.000Z'),
  );
  const response = await provider.register(new Request('https://example.test/account/register', {
    method: 'POST',
    body: JSON.stringify(validRegistration),
    headers: { 'Content-Type': 'application/json', 'CF-Connecting-IP': '203.0.113.10' },
  }));

  assert.equal(response.status, 429);
  assert.equal((await response.json()).error, 'too-many-attempts');
});
