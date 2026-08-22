import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  validateAccountProfile,
  createAccountProfile,
  deleteAccountProfile,
  fetchAccountProfile,
  normalizeUsername,
  isUsernameAvailable,
} from '../../src/auth/accountProfile.js';

const VALID = { username: 'joao_silva', firstName: 'João', lastName: "D'Ávila-Souza", termsVersion: '2026-08-02' };

test('validateAccountProfile accepts a well-formed profile and normalizes the username', () => {
  const result = validateAccountProfile(VALID);
  assert.deepEqual(result, {
    username: 'joao_silva',
    usernameNormalized: 'joao_silva',
    firstName: 'João',
    lastName: "D'Ávila-Souza",
    termsVersion: '2026-08-02',
  });
});

test('validateAccountProfile trims surrounding whitespace', () => {
  const result = validateAccountProfile({ username: '  joao_silva  ', firstName: '  João  ', lastName: '  Silva  ', termsVersion: '2026-08-02' });
  assert.equal(result.username, 'joao_silva');
  assert.equal(result.firstName, 'João');
  assert.equal(result.lastName, 'Silva');
});

test('validateAccountProfile lowercases usernameNormalized for case-insensitive uniqueness', () => {
  const result = validateAccountProfile({ ...VALID, username: 'JoaoSilva' });
  assert.equal(result.username, 'JoaoSilva');
  assert.equal(result.usernameNormalized, 'joaosilva');
});

test('validateAccountProfile rejects a payload that is not an object', () => {
  assert.equal(validateAccountProfile(null), null);
  assert.equal(validateAccountProfile('joao'), null);
  assert.equal(validateAccountProfile(42), null);
});

test('validateAccountProfile rejects a missing or non-string field', () => {
  assert.equal(validateAccountProfile({ firstName: 'João', lastName: 'Silva' }), null);
  assert.equal(validateAccountProfile({ ...VALID, username: 123 }), null);
});

test('validateAccountProfile requires the current terms version', () => {
  assert.equal(validateAccountProfile({ ...VALID, termsVersion: 'old' }), null);
  assert.equal(validateAccountProfile({ ...VALID, termsVersion: undefined }), null);
});

test('validateAccountProfile rejects a username shorter than 3 characters', () => {
  assert.equal(validateAccountProfile({ ...VALID, username: 'ab' }), null);
});

test('validateAccountProfile rejects a username longer than 24 characters', () => {
  assert.equal(validateAccountProfile({ ...VALID, username: 'a'.repeat(25) }), null);
});

test('validateAccountProfile rejects a username starting with a digit or underscore', () => {
  assert.equal(validateAccountProfile({ ...VALID, username: '1joao' }), null);
  assert.equal(validateAccountProfile({ ...VALID, username: '_joao' }), null);
});

test('validateAccountProfile rejects a username with a space or symbol other than underscore', () => {
  assert.equal(validateAccountProfile({ ...VALID, username: 'joao silva' }), null);
  assert.equal(validateAccountProfile({ ...VALID, username: 'joao-silva' }), null);
  assert.equal(validateAccountProfile({ ...VALID, username: 'joao@silva' }), null);
});

test('validateAccountProfile rejects an empty first or last name', () => {
  assert.equal(validateAccountProfile({ ...VALID, firstName: '' }), null);
  assert.equal(validateAccountProfile({ ...VALID, lastName: '   ' }), null);
});

test('validateAccountProfile rejects a first or last name longer than 60 characters', () => {
  assert.equal(validateAccountProfile({ ...VALID, firstName: 'a'.repeat(61) }), null);
});

test('validateAccountProfile rejects a name containing digits', () => {
  assert.equal(validateAccountProfile({ ...VALID, firstName: 'Jo4o' }), null);
});

test('validateAccountProfile accepts accented, hyphenated and apostrophe names', () => {
  const result = validateAccountProfile({ ...VALID, firstName: 'José', lastName: "O'Neil-Santos" });
  assert.equal(result.firstName, 'José');
  assert.equal(result.lastName, "O'Neil-Santos");
});

function fakeDb({ throwsWithMessage } = {}) {
  const inserted = [];
  return {
    inserted,
    prepare(sql) {
      return {
        bind(...params) {
          if (sql.startsWith('SELECT')) {
            return { async first() { return null; } };
          }
          return {
            async run() {
              if (throwsWithMessage) {
                throw new Error(throwsWithMessage);
              }
              inserted.push({ sql, params });
              return { success: true };
            },
          };
        },
      };
    },
  };
}

test('createAccountProfile inserts a row keyed by the verified uid, never a client-supplied one', async () => {
  const db = fakeDb();
  const profile = validateAccountProfile(VALID);
  const result = await createAccountProfile(db, 'firebase-uid-123', profile);
  assert.deepEqual(result, { ok: true });
  assert.equal(db.inserted.length, 1);
  assert.deepEqual(db.inserted[0].params, [
    'firebase-uid-123',
    profile.username,
    profile.usernameNormalized,
    profile.firstName,
    profile.lastName,
    profile.termsVersion,
    db.inserted[0].params[6],
    db.inserted[0].params[7],
  ]);
});

test('createAccountProfile maps a username uniqueness violation to username-taken', async () => {
  const db = fakeDb({ throwsWithMessage: 'UNIQUE constraint failed: idx_account_profiles_username_normalized' });
  const result = await createAccountProfile(db, 'uid', validateAccountProfile(VALID));
  assert.deepEqual(result, { ok: false, code: 'username-taken' });
});

test('createAccountProfile maps an unexpected D1 failure to unknown', async () => {
  const db = fakeDb({ throwsWithMessage: 'D1_ERROR: network timeout' });
  const result = await createAccountProfile(db, 'uid', validateAccountProfile(VALID));
  assert.deepEqual(result, { ok: false, code: 'unknown' });
});

function fakeReadDb(row) {
  const bound = [];
  return {
    bound,
    prepare(sql) {
      return {
        bind(...params) {
          bound.push({ sql, params });
          return { async first() { return row; } };
        },
      };
    },
  };
}

test('fetchAccountProfile maps the stored row to camelCase and reads by the given uid', async () => {
  const db = fakeReadDb({ username: 'joao_silva', first_name: 'João', last_name: "D'Ávila-Souza", terms_version: '2026-08-02' });
  const result = await fetchAccountProfile(db, 'firebase-uid-123');
  assert.deepEqual(result, { username: 'joao_silva', firstName: 'João', lastName: "D'Ávila-Souza", termsVersion: '2026-08-02' });
  assert.deepEqual(db.bound[0].params, ['firebase-uid-123']);
});

test('deleteAccountProfile scopes the deletion to the verified uid', async () => {
  const db = fakeDb();
  await deleteAccountProfile(db, 'firebase-uid-123');
  assert.deepEqual(db.inserted[0].params, ['firebase-uid-123']);
});

test('fetchAccountProfile returns null when the account has no profile row', async () => {
  const db = fakeReadDb(null);
  const result = await fetchAccountProfile(db, 'uid-without-profile');
  assert.equal(result, null);
});

test('normalizeUsername lowercases a valid username and trims it', () => {
  assert.equal(normalizeUsername('  JoaoSilva  '), 'joaosilva');
  assert.equal(normalizeUsername('a_1'), 'a_1');
});

test('normalizeUsername rejects anything createAccountProfile would also reject', () => {
  assert.equal(normalizeUsername('ab'), null, 'too short');
  assert.equal(normalizeUsername('a'.repeat(25)), null, 'too long');
  assert.equal(normalizeUsername('1joao'), null, 'must start with a letter');
  assert.equal(normalizeUsername('joao silva'), null, 'no spaces');
  assert.equal(normalizeUsername('joão_silva'), null, 'ASCII letters only');
  assert.equal(normalizeUsername(null), null);
  assert.equal(normalizeUsername(42), null);
});

test('isUsernameAvailable is true only when no row holds the normalized name', async () => {
  const free = fakeReadDb(null);
  assert.equal(await isUsernameAvailable(free, 'joao_silva'), true);
  assert.deepEqual(free.bound[0].params, ['joao_silva']);

  const taken = fakeReadDb({ taken: 1 });
  assert.equal(await isUsernameAvailable(taken, 'joao_silva'), false);
});
