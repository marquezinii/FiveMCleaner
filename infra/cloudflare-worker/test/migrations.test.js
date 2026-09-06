import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const workerRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const migrationSource = join(workerRoot, 'migrations');
const wrangler = join(workerRoot, 'node_modules', 'wrangler', 'bin', 'wrangler.js');
const migrationNames = (await readdir(migrationSource))
  .filter((name) => /^\d{4}_.+\.sql$/.test(name))
  .sort();
const historicalBootstrapMigrations = migrationNames.filter((name) => /^000[0-5]_/.test(name));

test('production config keeps the existing Cloudflare resource identifiers', async () => {
  const config = await readFile(join(workerRoot, 'wrangler.toml'), 'utf8');

  assert.match(config, /^name = "fivemcleaner-telemetry"\r?$/m);
  assert.match(config, /^DASHBOARD_ORIGIN = "https:\/\/ralven-dashboard\.pages\.dev,https:\/\/dashboard\.vemryx\.com,https:\/\/fivemcleaner-dashboard\.pages\.dev"\r?$/m);
  assert.match(config, /^database_name = "fivemcleaner-telemetry"\r?$/m);
  assert.match(config, /^database_id = "fe276121-a71a-4ba4-ab62-81cccdf601c6"\r?$/m);
});

function run(args, { expectSuccess = true } = {}) {
  const result = spawnSync(process.execPath, [wrangler, ...args], { cwd: workerRoot, encoding: 'utf8' });
  if (expectSuccess !== (result.status === 0)) {
    throw new Error(`wrangler ${args.join(' ')}\n${result.stdout}\n${result.stderr}`);
  }
  return result.stdout;
}

async function createFixture(root, name, migrations) {
  const directory = join(root, name);
  const migrationsDirectory = join(directory, 'migrations');
  await mkdir(migrationsDirectory, { recursive: true });
  await Promise.all(migrations.map(async (migration) => {
    const source = migration === '0006_atomic_failure.sql'
      ? 'CREATE TABLE migration_atomicity_probe (id INTEGER);\nTHIS IS NOT SQL;\n'
      : await readFile(join(migrationSource, migration), 'utf8');
    await writeFile(join(migrationsDirectory, migration), source);
  }));

  const config = join(directory, 'wrangler.toml');
  await writeFile(config, [
    'name = "d1-migration-test"',
    'main = "src/index.js"',
    'compatibility_date = "2026-08-11"',
    '',
    '[[d1_databases]]',
    'binding = "TELEMETRY_DB"',
    'database_name = "d1-migration-test"',
    'database_id = "00000000-0000-0000-0000-000000000000"',
    `migrations_dir = ${JSON.stringify(migrationsDirectory.replaceAll('\\', '/'))}`,
  ].join('\n'));

  return config;
}

function apply(config, stateDirectory, expectSuccess = true) {
  return run([
    'd1', 'migrations', 'apply', 'TELEMETRY_DB', '--local',
    '--persist-to', stateDirectory, '--config', config,
  ], { expectSuccess });
}

function execute(config, stateDirectory, command, expectSuccess = true) {
  const output = run([
    'd1', 'execute', 'TELEMETRY_DB', '--local',
    '--persist-to', stateDirectory, '--config', config,
    '--command', command, '--json',
  ], { expectSuccess });
  return expectSuccess ? JSON.parse(output) : output;
}

function adoptHistoricalBootstrap(config, stateDirectory) {
  const state = execute(config, stateDirectory, `
    SELECT
      EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'telemetry_events') AS has_schema,
      EXISTS(SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'd1_migrations') AS has_ledger,
      EXISTS(SELECT 1 FROM pragma_table_info('telemetry_events') WHERE name = 'process_count_at_start') AS has_v5_telemetry,
      EXISTS(SELECT 1 FROM pragma_table_info('account_profiles') WHERE name = 'terms_version') AS has_profile_terms;
  `)[0].results[0];

  if (!state.has_schema || state.has_ledger) {
    return;
  }

  assert.equal(state.has_v5_telemetry, 1, 'legacy bootstrap must have the v5 telemetry schema before adoption');
  assert.equal(state.has_profile_terms, 1, 'legacy bootstrap must have the profile terms schema before adoption');
  execute(config, stateDirectory, `
    CREATE TABLE IF NOT EXISTS d1_migrations (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name TEXT UNIQUE,
      applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL
    );
    INSERT OR IGNORE INTO d1_migrations (name) VALUES
      ${historicalBootstrapMigrations.map((name) => `('${name}')`).join(',\n      ')};
  `);
}

const workerSchemaSmoke = `
  INSERT INTO telemetry_events
    (event_name, execution_time_ms, app_version, bug_code, environment, received_at,
     five_m_install_detected, gta_edition, optimization_target_count,
     windows_build, disk_type, free_space_gib_bucket, run_timestamp,
     days_since_last_run_bucket, backup_created, backup_restored,
     elevation_used, process_count_at_start)
  VALUES
    ('OptimizationCompleted', 1, 'test', 'APP_OPT_ACTION_EXECUTION', 'Production', '2026-01-01T00:00:00.000Z',
     1, 'Legacy', 1, 26100, 'SSD', 1, '2026-01-01T00:00:00.000Z', 1, 1, 0, 0, 1);
  INSERT INTO bug_reports
    (report_id, category, bug_code, summary, description, app_version, profile, environment, received_at)
  VALUES
    ('report-1', 'optimization', 'APP_OPT_ACTION_EXECUTION', 'Test report',
     'A sufficiently detailed migration smoke report.', 'test', 'Balanced', 'Production',
     '2026-01-01T00:00:00.000Z');
  INSERT INTO account_profiles
    (uid, username, username_normalized, first_name, last_name, terms_version, terms_accepted_at, created_at)
  VALUES ('test-user', 'TestUser', 'testuser', 'Test', 'User', 'v1', '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');
  INSERT INTO billing_checkout_intents
    (id, account_uid, provider, external_reference, offer_key, amount_cents, currency,
     provider_checkout_id, state, created_at, updated_at)
  VALUES
    ('checkout-1', 'test-user', 'asaas', 'opaque-checkout-1', 'ralven_pro_monthly',
     1490, 'BRL', 'provider-checkout-1', 'completed', '2026-01-01T00:00:00.000Z',
     '2026-01-01T00:01:00.000Z');
  INSERT INTO billing_webhook_events
    (provider, provider_request_id, resource_id, received_at,
     processing_outcome, processed_at)
  VALUES
    ('asaas', 'request-1', 'provider-subscription-1',
     '2026-01-01T00:01:01.000Z', 'processed', '2026-01-01T00:01:02.000Z');
  INSERT INTO billing_subscriptions
    (id, account_uid, checkout_intent_id, provider, provider_subscription_id,
     offer_key, state, provider_updated_at, last_event_id, created_at, updated_at)
  VALUES
    ('subscription-1', 'test-user', 'checkout-1', 'asaas', 'provider-subscription-1',
     'ralven_pro_monthly', 'authorized', '2026-01-01T00:01:00.000Z',
     (SELECT id FROM billing_webhook_events WHERE provider = 'asaas' AND provider_request_id = 'request-1'),
     '2026-01-01T00:00:00.000Z', '2026-01-01T00:01:02.000Z');
  INSERT INTO account_entitlements
    (account_uid, entitlement_key, state, subscription_id, valid_from, valid_until,
     provider_updated_at, last_event_id, updated_at)
  VALUES
    ('test-user', 'ralven_pro', 'active', 'subscription-1', '2026-01-01T00:00:00.000Z',
     '2026-02-01T00:00:00.000Z', '2026-01-01T00:01:00.000Z',
     (SELECT id FROM billing_webhook_events WHERE provider = 'asaas' AND provider_request_id = 'request-1'),
     '2026-01-01T00:01:02.000Z');
  SELECT message, active, updated_at FROM live_alert WHERE id = 1;
  SELECT username, first_name, last_name, terms_version FROM account_profiles WHERE uid = 'test-user';
  SELECT entitlement_key, state, valid_until FROM account_entitlements WHERE account_uid = 'test-user';
`;

async function verifyUpgradeFrom(priorCount, t) {
  const root = await mkdtemp(join(tmpdir(), 'Ralven-d1-migrations-'));
  t.after(() => rm(root, { recursive: true, force: true }));

  const currentConfig = await createFixture(root, 'current', migrationNames);
  const stateDirectory = join(root, 'state');
  if (priorCount > 0) {
    const oldConfig = await createFixture(root, 'prior', migrationNames.slice(0, priorCount));
    apply(oldConfig, stateDirectory);
  }

  apply(currentConfig, stateDirectory);
  execute(currentConfig, stateDirectory, workerSchemaSmoke);
}

for (let priorCount = 0; priorCount < migrationNames.length; priorCount += 1) {
  const source = priorCount === 0 ? 'an empty database' : `the ${migrationNames[priorCount - 1]} schema`;
  test(`D1 migrations upgrade ${source} to the current Worker contract`, (t) => verifyUpgradeFrom(priorCount, t));
}

test('D1 migrations adopt the historical schema.sql bootstrap before applying newer migrations', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'Ralven-d1-legacy-bootstrap-'));
  t.after(() => rm(root, { recursive: true, force: true }));

  const stateDirectory = join(root, 'state');
  const legacyConfig = await createFixture(root, 'legacy', historicalBootstrapMigrations);
  apply(legacyConfig, stateDirectory);
  execute(legacyConfig, stateDirectory, 'DROP TABLE d1_migrations;');

  adoptHistoricalBootstrap(legacyConfig, stateDirectory);
  const currentConfig = await createFixture(root, 'current', migrationNames);
  apply(currentConfig, stateDirectory);
  execute(currentConfig, stateDirectory, workerSchemaSmoke);
});

test('billing migration enforces ownership, deduplicates events, and cascades account deletion', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'Ralven-d1-billing-'));
  t.after(() => rm(root, { recursive: true, force: true }));

  const stateDirectory = join(root, 'state');
  const config = await createFixture(root, 'current', migrationNames);
  apply(config, stateDirectory);

  const result = execute(config, stateDirectory, `
    INSERT INTO account_profiles
      (uid, username, username_normalized, first_name, last_name, terms_version, terms_accepted_at, created_at)
    VALUES
      ('billing-user', 'BillingUser', 'billinguser', 'Billing', 'User', 'v1',
       '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z'),
      ('billing-other', 'BillingOther', 'billingother', 'Billing', 'Other', 'v1',
       '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');
    INSERT INTO billing_checkout_intents
      (id, account_uid, provider, external_reference, offer_key, amount_cents, currency,
       state, created_at, updated_at)
    VALUES
      ('checkout-valid', 'billing-user', 'asaas', 'opaque-valid',
       'ralven_pro_monthly', 1490, 'BRL', 'pending',
       '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');
    INSERT OR IGNORE INTO billing_checkout_intents
      (id, account_uid, provider, external_reference, offer_key, amount_cents, currency,
       state, created_at, updated_at)
    VALUES
      ('checkout-replay', 'billing-user', 'asaas', 'opaque-valid',
       'ralven_pro_monthly', 1490, 'BRL', 'pending',
       '2026-01-01T00:00:01.000Z', '2026-01-01T00:00:01.000Z'),
      ('checkout-invalid-state', 'billing-user', 'asaas', 'opaque-invalid',
       'ralven_pro_monthly', 1490, 'BRL', 'unknown',
       '2026-01-01T00:00:01.000Z', '2026-01-01T00:00:01.000Z');
    INSERT INTO billing_webhook_events
      (provider, provider_request_id, resource_id, received_at)
    VALUES
      ('asaas', 'request-late', 'provider-subscription-1',
       '2026-01-01T00:02:02.000Z'),
      ('asaas', 'request-early', 'provider-subscription-1',
       '2026-01-01T00:02:01.000Z');
    INSERT OR IGNORE INTO billing_webhook_events
      (provider, provider_request_id, resource_id, received_at)
    VALUES
      ('asaas', 'request-late', 'different-resource',
       '2026-01-01T00:03:00.000Z');
    INSERT INTO billing_subscriptions
      (id, account_uid, checkout_intent_id, provider, provider_subscription_id,
       offer_key, state, provider_updated_at, last_event_id, created_at, updated_at)
    VALUES
      ('subscription-valid', 'billing-user', 'checkout-valid', 'asaas',
       'provider-subscription-1', 'ralven_pro_monthly', 'authorized',
       '2026-01-01T00:02:00.000Z',
       (SELECT id FROM billing_webhook_events WHERE provider = 'asaas' AND provider_request_id = 'request-late'),
       '2026-01-01T00:00:00.000Z', '2026-01-01T00:02:02.000Z');
    INSERT INTO account_entitlements
      (account_uid, entitlement_key, state, subscription_id, valid_from, valid_until,
       provider_updated_at, last_event_id, updated_at)
    VALUES
      ('billing-user', 'ralven_pro', 'active', 'subscription-valid',
       '2026-01-01T00:00:00.000Z', '2026-02-01T00:00:00.000Z',
       '2026-01-01T00:02:00.000Z',
       (SELECT id FROM billing_webhook_events WHERE provider = 'asaas' AND provider_request_id = 'request-late'),
       '2026-01-01T00:02:02.000Z');
    SELECT id FROM billing_checkout_intents ORDER BY id;
    SELECT provider_request_id FROM billing_webhook_events
      WHERE provider = 'asaas' AND resource_id = 'provider-subscription-1'
      ORDER BY received_at, id;
    SELECT name FROM sqlite_master
      WHERE type = 'index' AND name IN (
        'idx_billing_checkout_intents_account_contract',
        'idx_billing_subscriptions_account_contract'
      )
      ORDER BY name;
  `);

  assert.deepEqual(result.at(-3).results, [{ id: 'checkout-valid' }]);
  assert.deepEqual(result.at(-2).results, [
    { provider_request_id: 'request-early' },
    { provider_request_id: 'request-late' },
  ]);
  assert.deepEqual(result.at(-1).results.map(({ name }) => name), [
    'idx_billing_checkout_intents_account_contract',
    'idx_billing_subscriptions_account_contract',
  ]);

  execute(config, stateDirectory, `
    INSERT INTO billing_subscriptions
      (id, account_uid, checkout_intent_id, provider, provider_subscription_id,
       offer_key, state, provider_updated_at, created_at, updated_at)
    VALUES
      ('subscription-cross-account', 'billing-other', 'checkout-valid', 'asaas',
       'provider-subscription-cross', 'ralven_pro_monthly', 'authorized',
       '2026-01-01T00:03:00.000Z', '2026-01-01T00:03:00.000Z',
       '2026-01-01T00:03:00.000Z');
  `, false);
  execute(config, stateDirectory, `
    INSERT INTO account_entitlements
      (account_uid, entitlement_key, state, subscription_id, valid_from, valid_until,
       provider_updated_at, updated_at)
    VALUES
      ('billing-other', 'ralven_pro', 'active', 'subscription-valid',
       '2026-01-01T00:00:00.000Z', '2026-02-01T00:00:00.000Z',
       '2026-01-01T00:03:00.000Z', '2026-01-01T00:03:00.000Z');
  `, false);
  const invalidOwnership = execute(config, stateDirectory, `
    SELECT
      (SELECT COUNT(*) FROM billing_subscriptions WHERE id = 'subscription-cross-account') AS subscription_count,
      (SELECT COUNT(*) FROM account_entitlements WHERE account_uid = 'billing-other') AS entitlement_count;
  `);
  assert.deepEqual(invalidOwnership.at(-1).results, [{ subscription_count: 0, entitlement_count: 0 }]);

  const deletion = execute(config, stateDirectory, `
    DELETE FROM account_profiles WHERE uid = 'billing-user';
    SELECT
      (SELECT COUNT(*) FROM billing_checkout_intents WHERE account_uid = 'billing-user') AS checkout_count,
      (SELECT COUNT(*) FROM billing_subscriptions WHERE account_uid = 'billing-user') AS subscription_count,
      (SELECT COUNT(*) FROM account_entitlements WHERE account_uid = 'billing-user') AS entitlement_count,
      (SELECT COUNT(*) FROM billing_webhook_events WHERE provider = 'asaas') AS webhook_count;
  `);
  assert.deepEqual(deletion.at(-1).results, [{
    checkout_count: 0,
    subscription_count: 0,
    entitlement_count: 0,
    webhook_count: 2,
  }]);
});

test('a failed D1 migration is atomic and is not recorded as applied', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'Ralven-d1-atomicity-'));
  t.after(() => rm(root, { recursive: true, force: true }));

  const stateDirectory = join(root, 'state');
  const currentConfig = await createFixture(root, 'current', migrationNames);
  apply(currentConfig, stateDirectory);
  const config = await createFixture(root, 'atomicity', [...migrationNames, '0006_atomic_failure.sql']);
  apply(config, stateDirectory, false);

  const result = execute(config, stateDirectory, `
    SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'migration_atomicity_probe';
    SELECT name FROM d1_migrations WHERE name = '0006_atomic_failure.sql';
  `);
  assert.deepEqual(result.flatMap((statement) => statement.results), []);
});
