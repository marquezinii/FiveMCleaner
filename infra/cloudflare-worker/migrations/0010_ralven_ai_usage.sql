-- Monthly AI spend ledger. Prompts, replies, diagnostic snapshots, tokens,
-- provider credentials, and account email are deliberately not persisted.
CREATE TABLE IF NOT EXISTS ralven_ai_usage (
    request_id TEXT PRIMARY KEY NOT NULL,
    account_uid TEXT NOT NULL REFERENCES account_profiles (uid) ON DELETE CASCADE,
    billing_period TEXT NOT NULL CHECK (
        length(billing_period) = 7 AND substr(billing_period, 5, 1) = '-'
    ),
    state TEXT NOT NULL CHECK (state IN ('reserved', 'completed', 'failed')),
    reserved_cost_microusd INTEGER NOT NULL CHECK (reserved_cost_microusd > 0),
    actual_cost_microusd INTEGER CHECK (actual_cost_microusd IS NULL OR actual_cost_microusd >= 0),
    input_tokens INTEGER CHECK (input_tokens IS NULL OR input_tokens >= 0),
    output_tokens INTEGER CHECK (output_tokens IS NULL OR output_tokens >= 0),
    created_at TEXT NOT NULL,
    completed_at TEXT,
    CHECK (
        (state = 'reserved' AND completed_at IS NULL)
        OR (state <> 'reserved' AND completed_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS idx_ralven_ai_usage_account_period
    ON ralven_ai_usage (account_uid, billing_period);
CREATE INDEX IF NOT EXISTS idx_ralven_ai_usage_period
    ON ralven_ai_usage (billing_period);
