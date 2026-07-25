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
    received_at TEXT NOT NULL
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
