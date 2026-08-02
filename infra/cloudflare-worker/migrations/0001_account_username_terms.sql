-- The deployed baseline already contains user_accounts. The account table was
-- confirmed empty before this migration was authored, so these new required
-- registration fields need no legacy backfill.
ALTER TABLE user_accounts ADD COLUMN username_normalized TEXT;
ALTER TABLE user_accounts ADD COLUMN terms_version TEXT;
ALTER TABLE user_accounts ADD COLUMN terms_accepted_at TEXT;

CREATE UNIQUE INDEX idx_user_accounts_username_normalized
    ON user_accounts (username_normalized);

CREATE TRIGGER user_accounts_require_username_terms_insert
BEFORE INSERT ON user_accounts
WHEN NEW.username_normalized IS NULL OR NEW.terms_version IS NULL OR NEW.terms_accepted_at IS NULL
BEGIN
    SELECT RAISE(ABORT, 'account username and terms acceptance are required');
END;

CREATE TRIGGER user_accounts_require_username_terms_update
BEFORE UPDATE OF username_normalized, terms_version, terms_accepted_at ON user_accounts
WHEN NEW.username_normalized IS NULL OR NEW.terms_version IS NULL OR NEW.terms_accepted_at IS NULL
BEGIN
    SELECT RAISE(ABORT, 'account username and terms acceptance are required');
END;

CREATE TABLE user_registration_attempts (
    ip_hash TEXT PRIMARY KEY,
    account_count INTEGER NOT NULL DEFAULT 0,
    window_started_at TEXT NOT NULL
);
