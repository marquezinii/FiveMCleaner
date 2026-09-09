-- Complete the pre-provider Ralven AI accounting contract without storing
-- prompts, replies, diagnostic snapshots, credentials, or account email.
ALTER TABLE ralven_ai_usage ADD COLUMN cached_input_tokens INTEGER
    CHECK (cached_input_tokens IS NULL OR cached_input_tokens >= 0);
ALTER TABLE ralven_ai_usage ADD COLUMN cache_write_tokens INTEGER
    CHECK (cache_write_tokens IS NULL OR cache_write_tokens >= 0);
ALTER TABLE ralven_ai_usage ADD COLUMN reasoning_tokens INTEGER
    CHECK (reasoning_tokens IS NULL OR reasoning_tokens >= 0);

CREATE INDEX IF NOT EXISTS idx_ralven_ai_usage_created_at
    ON ralven_ai_usage (created_at);

-- Existing paid Pro periods receive the separate AI entitlement. A Pro grant
-- without a canonical approved payment remains Pro-only by design.
INSERT INTO account_entitlements
    (account_uid, entitlement_key, state, subscription_id, valid_from, valid_until,
     provider_updated_at, last_event_id, updated_at)
SELECT e.account_uid, 'ralven_ai', e.state, e.subscription_id, e.valid_from, e.valid_until,
       e.provider_updated_at, e.last_event_id, e.updated_at
FROM account_entitlements e
WHERE e.entitlement_key = 'ralven_pro'
  AND EXISTS (
      SELECT 1
      FROM billing_payments p
      WHERE p.subscription_id = e.subscription_id
        AND p.state = 'approved'
        AND p.refunded_cents = 0
        AND p.period_start = e.valid_from
        AND p.period_end = e.valid_until
  )
ON CONFLICT(account_uid, entitlement_key) DO NOTHING;
