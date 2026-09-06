-- Billing state is keyed only by the Firebase UID already verified by the
-- Worker. Provider references are opaque identifiers; no email, checkout URL,
-- webhook body, credential, or secret is persisted here.

CREATE TABLE IF NOT EXISTS billing_checkout_intents (
    id TEXT PRIMARY KEY NOT NULL,
    account_uid TEXT NOT NULL REFERENCES account_profiles (uid) ON DELETE CASCADE,
    provider TEXT NOT NULL CHECK (length(provider) BETWEEN 1 AND 32),
    external_reference TEXT NOT NULL UNIQUE CHECK (length(external_reference) BETWEEN 1 AND 128),
    offer_key TEXT NOT NULL CHECK (length(offer_key) BETWEEN 1 AND 64),
    amount_cents INTEGER NOT NULL CHECK (amount_cents > 0),
    currency TEXT NOT NULL CHECK (currency = 'BRL'),
    provider_checkout_id TEXT CHECK (provider_checkout_id IS NULL OR length(provider_checkout_id) BETWEEN 1 AND 128),
    state TEXT NOT NULL CHECK (state IN ('created', 'pending', 'completed', 'cancelled')),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    CHECK (updated_at >= created_at)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_billing_checkout_intents_provider_checkout
    ON billing_checkout_intents (provider, provider_checkout_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_billing_checkout_intents_account_contract
    ON billing_checkout_intents (id, account_uid, provider, offer_key);

-- Persist only the authenticated event envelope plus internal processing
-- state. Resource details are fetched from the provider before any grant.
CREATE TABLE IF NOT EXISTS billing_webhook_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider TEXT NOT NULL CHECK (length(provider) BETWEEN 1 AND 32),
    provider_request_id TEXT NOT NULL CHECK (length(provider_request_id) BETWEEN 1 AND 128),
    resource_id TEXT NOT NULL CHECK (length(resource_id) BETWEEN 1 AND 128),
    received_at TEXT NOT NULL,
    processing_outcome TEXT NOT NULL DEFAULT 'pending'
        CHECK (processing_outcome IN ('pending', 'processed', 'ignored', 'failed')),
    processed_at TEXT,
    UNIQUE (provider, provider_request_id),
    CHECK (
        (processing_outcome = 'pending' AND processed_at IS NULL)
        OR (processing_outcome <> 'pending' AND processed_at IS NOT NULL)
    )
);

CREATE TABLE IF NOT EXISTS billing_subscriptions (
    id TEXT PRIMARY KEY NOT NULL,
    account_uid TEXT NOT NULL REFERENCES account_profiles (uid) ON DELETE CASCADE,
    checkout_intent_id TEXT NOT NULL UNIQUE,
    provider TEXT NOT NULL CHECK (length(provider) BETWEEN 1 AND 32),
    provider_subscription_id TEXT NOT NULL CHECK (length(provider_subscription_id) BETWEEN 1 AND 128),
    offer_key TEXT NOT NULL CHECK (length(offer_key) BETWEEN 1 AND 64),
    state TEXT NOT NULL CHECK (state IN ('pending', 'authorized', 'paused', 'cancelled')),
    provider_updated_at TEXT NOT NULL
        CHECK (length(provider_updated_at) = 24 AND substr(provider_updated_at, 5, 1) = '-' AND substr(provider_updated_at, 8, 1) = '-' AND substr(provider_updated_at, 11, 1) = 'T' AND substr(provider_updated_at, 14, 1) = ':' AND substr(provider_updated_at, 17, 1) = ':' AND substr(provider_updated_at, 20, 1) = '.' AND substr(provider_updated_at, 24, 1) = 'Z'),
    last_event_id INTEGER REFERENCES billing_webhook_events (id),
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    UNIQUE (provider, provider_subscription_id),
    CHECK (updated_at >= created_at),
    FOREIGN KEY (checkout_intent_id, account_uid, provider, offer_key)
        REFERENCES billing_checkout_intents (id, account_uid, provider, offer_key)
        ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_billing_subscriptions_account_contract
    ON billing_subscriptions (id, account_uid);

-- This is the server-authoritative access snapshot read by the app. Paid
-- access is always time-bounded and traceable to a normalized subscription
-- plus the webhook event that most recently changed it.
CREATE TABLE IF NOT EXISTS account_entitlements (
    account_uid TEXT NOT NULL REFERENCES account_profiles (uid) ON DELETE CASCADE,
    entitlement_key TEXT NOT NULL CHECK (length(entitlement_key) BETWEEN 1 AND 64),
    state TEXT NOT NULL CHECK (state IN ('active', 'grace_period', 'expired', 'revoked')),
    subscription_id TEXT NOT NULL,
    valid_from TEXT NOT NULL
        CHECK (length(valid_from) = 24 AND substr(valid_from, 5, 1) = '-' AND substr(valid_from, 8, 1) = '-' AND substr(valid_from, 11, 1) = 'T' AND substr(valid_from, 14, 1) = ':' AND substr(valid_from, 17, 1) = ':' AND substr(valid_from, 20, 1) = '.' AND substr(valid_from, 24, 1) = 'Z'),
    valid_until TEXT NOT NULL
        CHECK (length(valid_until) = 24 AND substr(valid_until, 5, 1) = '-' AND substr(valid_until, 8, 1) = '-' AND substr(valid_until, 11, 1) = 'T' AND substr(valid_until, 14, 1) = ':' AND substr(valid_until, 17, 1) = ':' AND substr(valid_until, 20, 1) = '.' AND substr(valid_until, 24, 1) = 'Z'),
    provider_updated_at TEXT NOT NULL
        CHECK (length(provider_updated_at) = 24 AND substr(provider_updated_at, 5, 1) = '-' AND substr(provider_updated_at, 8, 1) = '-' AND substr(provider_updated_at, 11, 1) = 'T' AND substr(provider_updated_at, 14, 1) = ':' AND substr(provider_updated_at, 17, 1) = ':' AND substr(provider_updated_at, 20, 1) = '.' AND substr(provider_updated_at, 24, 1) = 'Z'),
    last_event_id INTEGER REFERENCES billing_webhook_events (id),
    updated_at TEXT NOT NULL,
    PRIMARY KEY (account_uid, entitlement_key),
    CHECK (valid_until >= valid_from),
    FOREIGN KEY (subscription_id, account_uid)
        REFERENCES billing_subscriptions (id, account_uid)
        ON DELETE CASCADE
);
