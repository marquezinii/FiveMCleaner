-- Schema for the FiveMCleaner anonymous telemetry D1 database.
-- Mirrors the closed allowlist documented in docs/telemetry.md: no file
-- paths, machine identifiers, or free text are ever columns here.

CREATE TABLE IF NOT EXISTS telemetry_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_name TEXT NOT NULL,
    execution_time_ms INTEGER NOT NULL,
    app_version TEXT NOT NULL,
    error_category TEXT,
    environment TEXT NOT NULL,
    received_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_telemetry_events_received_at
    ON telemetry_events (received_at);

CREATE INDEX IF NOT EXISTS idx_telemetry_events_environment
    ON telemetry_events (environment);
