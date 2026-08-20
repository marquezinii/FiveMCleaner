-- Schema for the FiveMCleaner anonymous telemetry D1 database.
-- Mirrors the closed allowlist documented in docs/telemetry.md (version 2 of
-- the privacy consent): no file paths, no machine identifiers, no free
-- text -- CPU/GPU model and RAM bucket are coarse hardware categories, not
-- unique machine fingerprints.

CREATE TABLE IF NOT EXISTS telemetry_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_name TEXT NOT NULL,
    execution_time_ms INTEGER NOT NULL,
    app_version TEXT NOT NULL,
    error_category TEXT,
    os_version TEXT,
    system_architecture TEXT,
    cpu_model TEXT,
    gpu_model TEXT,
    ram_bucket_gib INTEGER,
    profile TEXT,
    environment TEXT NOT NULL,
    received_at TEXT NOT NULL,
    -- v5: expanded diagnostic fields, optional -- see migrations/0004_telemetry_v5_fields.sql
    -- and PROJECT_STATE.md item 9. No client sends these yet.
    five_m_install_detected INTEGER,
    gta_edition TEXT,
    optimization_target_count INTEGER,
    windows_build INTEGER,
    disk_type TEXT,
    free_space_gib_bucket INTEGER,
    run_timestamp TEXT,
    days_since_last_run_bucket INTEGER,
    backup_created INTEGER,
    backup_restored INTEGER,
    elevation_used INTEGER,
    process_count_at_start INTEGER
);

CREATE INDEX IF NOT EXISTS idx_telemetry_events_received_at
    ON telemetry_events (received_at);

CREATE INDEX IF NOT EXISTS idx_telemetry_events_environment
    ON telemetry_events (environment);

CREATE INDEX IF NOT EXISTS idx_telemetry_events_app_version
    ON telemetry_events (app_version);

-- One row per action ID applied in an optimization-completed event, so
-- "most used function" can be aggregated with a simple GROUP BY instead of
-- unpacking a JSON array in every query.
CREATE TABLE IF NOT EXISTS telemetry_event_actions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    telemetry_event_id INTEGER NOT NULL REFERENCES telemetry_events (id),
    action_id TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_telemetry_event_actions_action_id
    ON telemetry_event_actions (action_id);

CREATE INDEX IF NOT EXISTS idx_telemetry_event_actions_event_id
    ON telemetry_event_actions (telemetry_event_id);

-- Admin dashboard authentication (custom, no external identity provider —
-- see infra/cloudflare-worker/README.md for the design rationale). IPs are
-- never stored in the clear: only an HMAC of the IP (keyed by the
-- IP_HASH_SECRET Worker secret) is kept, just enough to rate-limit repeated
-- failures without retaining a reversible identifier.
CREATE TABLE IF NOT EXISTS login_attempts (
    ip_hash TEXT PRIMARY KEY,
    failed_count INTEGER NOT NULL DEFAULT 0,
    first_failed_at TEXT NOT NULL,
    locked_until TEXT
);

-- Server-side admin sessions: an opaque, random session ID is the only
-- thing stored in the browser cookie, so a session can always be revoked
-- (logout, or manually clearing this table) instead of having to wait out
-- a stateless token's expiry.
CREATE TABLE IF NOT EXISTS admin_sessions (
    id TEXT PRIMARY KEY,
    created_at TEXT NOT NULL,
    expires_at TEXT NOT NULL,
    revoked_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_admin_sessions_expires_at
    ON admin_sessions (expires_at);

-- Text-only bug reports, replacing the previous FormSubmit-based flow. There
-- is no attachment/screenshot support and no R2 dependency.
CREATE TABLE IF NOT EXISTS bug_reports (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    report_id TEXT NOT NULL UNIQUE,
    category TEXT NOT NULL,
    summary TEXT NOT NULL,
    description TEXT NOT NULL,
    app_version TEXT NOT NULL,
    profile TEXT NOT NULL,
    technical_summary TEXT,
    email TEXT,
    log_text TEXT,
    environment TEXT NOT NULL,
    received_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_bug_reports_received_at
    ON bug_reports (received_at);

CREATE INDEX IF NOT EXISTS idx_bug_reports_environment
    ON bug_reports (environment);

CREATE TABLE IF NOT EXISTS updater_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT NOT NULL UNIQUE,
    stage TEXT NOT NULL,
    outcome TEXT NOT NULL,
    error_code TEXT NOT NULL,
    previous_version TEXT,
    candidate_version TEXT NOT NULL,
    environment TEXT NOT NULL,
    received_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_updater_events_received_at ON updater_events (received_at);
CREATE INDEX IF NOT EXISTS idx_updater_events_candidate_version ON updater_events (candidate_version);

-- Profile-completion data for a Firebase-authenticated account. Firebase
-- Authentication REST only owns email/password/uid -- it has no username,
-- first name, or last name -- so this is the sole place a username's
-- uniqueness is enforced (see docs/architecture.md and
-- src/auth/accountProfile.js). `uid` is the Firebase UID (JWT `sub`),
-- validated server-side by src/auth/firebaseIdToken.js; never trust a
-- client-supplied identifier here.
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

-- Single-row broadcast the admin dashboard writes to and the desktop app
-- polls (startup + hourly) -- see
-- docs/superpowers/specs/2026-08-17-live-alerts-design.md. There is only
-- ever one active alert at a time; id is always 1, seeded below.
CREATE TABLE IF NOT EXISTS live_alert (
    id INTEGER PRIMARY KEY,
    message TEXT NOT NULL DEFAULT '',
    active INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT OR IGNORE INTO live_alert (id, message, active, updated_at)
    VALUES (1, '', 0, datetime('now'));
