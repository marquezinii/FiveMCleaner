ALTER TABLE live_alert
    ADD COLUMN severity TEXT NOT NULL DEFAULT 'important'
    CHECK (severity IN ('info', 'important', 'critical'));
