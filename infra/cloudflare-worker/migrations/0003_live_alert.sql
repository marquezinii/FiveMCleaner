-- Single-row broadcast the admin dashboard writes to and the desktop app
-- polls (startup + hourly) -- see
-- docs/superpowers/specs/2026-08-17-live-alerts-design.md and schema.sql
-- for the full rationale.
CREATE TABLE IF NOT EXISTS live_alert (
    id INTEGER PRIMARY KEY,
    message TEXT NOT NULL DEFAULT '',
    active INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT OR IGNORE INTO live_alert (id, message, active, updated_at)
    VALUES (1, '', 0, datetime('now'));
