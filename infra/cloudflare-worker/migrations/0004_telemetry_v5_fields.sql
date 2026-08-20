-- Additive migration for the D1 database already in production: adds the
-- v5 expanded diagnostic columns to telemetry_events without touching
-- existing rows (all new columns are nullable). See schema.sql for the
-- fresh-install shape and PROJECT_STATE.md item 9 for context. No client
-- sends these fields yet -- this only makes the column exist so a future
-- client change can start writing to it without a further migration.
ALTER TABLE telemetry_events ADD COLUMN five_m_install_detected INTEGER;
ALTER TABLE telemetry_events ADD COLUMN gta_edition TEXT;
ALTER TABLE telemetry_events ADD COLUMN optimization_target_count INTEGER;
ALTER TABLE telemetry_events ADD COLUMN windows_build INTEGER;
ALTER TABLE telemetry_events ADD COLUMN disk_type TEXT;
ALTER TABLE telemetry_events ADD COLUMN free_space_gib_bucket INTEGER;
ALTER TABLE telemetry_events ADD COLUMN run_timestamp TEXT;
ALTER TABLE telemetry_events ADD COLUMN days_since_last_run_bucket INTEGER;
ALTER TABLE telemetry_events ADD COLUMN backup_created INTEGER;
ALTER TABLE telemetry_events ADD COLUMN backup_restored INTEGER;
ALTER TABLE telemetry_events ADD COLUMN elevation_used INTEGER;
ALTER TABLE telemetry_events ADD COLUMN process_count_at_start INTEGER;
