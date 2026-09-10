CREATE TABLE account_mfa_recovery_codes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    account_uid TEXT NOT NULL REFERENCES account_profiles(uid) ON DELETE CASCADE,
    enrollment_tag TEXT NOT NULL CHECK(length(enrollment_tag) = 64),
    code_hash TEXT NOT NULL CHECK(length(code_hash) = 64),
    generation_id TEXT NOT NULL CHECK(length(generation_id) = 36),
    created_at TEXT NOT NULL,
    reserved_until TEXT,
    used_at TEXT,
    UNIQUE(enrollment_tag, code_hash),
    CHECK(used_at IS NULL OR reserved_until IS NULL)
);

CREATE INDEX idx_account_mfa_recovery_lookup
    ON account_mfa_recovery_codes(enrollment_tag, code_hash, used_at);

CREATE TABLE account_auth_cutoffs (
    account_uid TEXT PRIMARY KEY,
    valid_after INTEGER NOT NULL CHECK(valid_after > 0),
    updated_at TEXT NOT NULL
);

CREATE TABLE account_deletion_jobs (
    account_uid TEXT PRIMARY KEY,
    requested_at TEXT NOT NULL
);
