-- v9 adds an optional, per-optimization UUID. It links a started flow to its
-- terminal result without identifying an installation, user or machine.
ALTER TABLE telemetry_events ADD COLUMN operation_id TEXT;
CREATE INDEX idx_telemetry_events_operation_id ON telemetry_events (operation_id);
