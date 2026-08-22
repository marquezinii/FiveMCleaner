-- Existing profiles must explicitly accept the current terms on their next
-- authenticated use. New profiles always receive both values server-side.
ALTER TABLE account_profiles ADD COLUMN terms_version TEXT;
ALTER TABLE account_profiles ADD COLUMN terms_accepted_at TEXT;
