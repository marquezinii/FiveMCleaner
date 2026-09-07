-- One unresolved checkout per account. Cancelled checkouts and paid intervals
-- remain auditable until the user explicitly deletes their account.
CREATE UNIQUE INDEX idx_billing_one_open_checkout
    ON billing_checkout_intents(account_uid) WHERE state <> 'cancelled';
ALTER TABLE billing_checkout_intents ADD COLUMN create_attempt_started_at TEXT;

CREATE TABLE billing_payments (
    provider_payment_id TEXT PRIMARY KEY NOT NULL,
    subscription_id TEXT NOT NULL REFERENCES billing_subscriptions(id) ON DELETE CASCADE,
    state TEXT NOT NULL CHECK(state IN ('approved', 'pending', 'rejected', 'refunded', 'cancelled', 'charged_back')),
    amount_cents INTEGER NOT NULL CHECK(amount_cents > 0),
    refunded_cents INTEGER NOT NULL CHECK(refunded_cents >= 0),
    currency TEXT NOT NULL CHECK(currency = 'BRL'),
    period_start TEXT NOT NULL,
    period_end TEXT NOT NULL CHECK(period_end > period_start),
    provider_updated_at TEXT NOT NULL,
    last_event_id INTEGER NOT NULL REFERENCES billing_webhook_events(id),
    updated_at TEXT NOT NULL
);
CREATE INDEX idx_billing_payment_subscription_period
    ON billing_payments(subscription_id, period_start, period_end);
