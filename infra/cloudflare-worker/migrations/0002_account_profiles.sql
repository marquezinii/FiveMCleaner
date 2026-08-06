-- Profile-completion data for a Firebase-authenticated account (Nome,
-- Sobrenome, unique Usuario). Firebase Authentication REST does not manage
-- any of these -- see src/auth/accountProfile.js and schema.sql for the
-- full rationale.
CREATE TABLE IF NOT EXISTS account_profiles (
    uid TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    username_normalized TEXT NOT NULL,
    first_name TEXT NOT NULL,
    last_name TEXT NOT NULL,
    created_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_account_profiles_username_normalized
    ON account_profiles (username_normalized);
