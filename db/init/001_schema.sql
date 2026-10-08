-- Pickleball Club — initial schema (PostgreSQL 16)
-- Runs automatically on first start of the postgres container (docker-entrypoint-initdb.d).
-- One Club per database. Times are stored as timestamptz (UTC); the Club's IANA timezone lives in club_settings.
-- Vocabulary follows CONTEXT.md.

CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ---------------------------------------------------------------- club
-- Exactly one row (id is always true).
CREATE TABLE club_settings (
    id                      boolean PRIMARY KEY DEFAULT true CHECK (id),
    name                    text NOT NULL,
    timezone                text NOT NULL DEFAULT 'Asia/Bangkok',
    -- Booking Window: a Customer may book today .. today + booking_window_days.
    booking_window_days     int NOT NULL DEFAULT 14 CHECK (booking_window_days BETWEEN 1 AND 365),
    -- A Reschedule must be made at least this many hours before play.
    reschedule_notice_hours int NOT NULL DEFAULT 48 CHECK (reschedule_notice_hours >= 0),
    updated_at              timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------- people
-- Customers and Admins. A Walk-in Guest has no row here (see bookings.guest_*).
CREATE TABLE users (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email               text NOT NULL UNIQUE CHECK (email = lower(email)),
    password_hash       text,               -- bcrypt; NULL for an account that only signs in with Google
    google_subject      text UNIQUE,
    display_name        text,
    phone               text,               -- required before a Customer's first Booking (enforced by the API)
    role                text NOT NULL DEFAULT 'customer' CHECK (role IN ('customer','admin')),
    email_verified_at   timestamptz,
    password_changed_at timestamptz,        -- rides in the JWT as a stamp, so a new password signs out every older session
    failed_logins       int NOT NULL DEFAULT 0,   -- consecutive wrong passwords; reset on success or when the lock is set
    locked_until        timestamptz,              -- sign-in refused until then (5 failures → 15 min)
    created_at          timestamptz NOT NULL DEFAULT now(),
    CHECK (password_hash IS NOT NULL OR google_subject IS NOT NULL)
);

-- One-time links sent by email (password reset, email verification). Only the SHA-256 of the token is stored.
CREATE TABLE user_tokens (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    purpose     text NOT NULL CHECK (purpose IN ('reset_password','verify_email')),
    token_hash  text NOT NULL UNIQUE,
    expires_at  timestamptz NOT NULL,
    consumed_at timestamptz,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_user_tokens_user ON user_tokens (user_id, purpose);

-- ---------------------------------------------------------------- courts & hours
CREATE TABLE courts (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name       text NOT NULL UNIQUE,
    indoor     boolean NOT NULL DEFAULT false,
    sort_order int NOT NULL DEFAULT 0,
    is_active  boolean NOT NULL DEFAULT true
);

-- Operating Hours, one row per open day of the week; no row = closed all day.
-- day_of_week: 0 = Sunday … 6 = Saturday (matches .NET DayOfWeek).
-- Hours are whole hours of the Club's local day (close_hour 24 = midnight), so every Slot starts on the hour.
CREATE TABLE operating_hours (
    day_of_week smallint PRIMARY KEY CHECK (day_of_week BETWEEN 0 AND 6),
    open_hour   smallint NOT NULL CHECK (open_hour BETWEEN 0 AND 23),
    close_hour  smallint NOT NULL CHECK (close_hour BETWEEN 1 AND 24),
    CHECK (close_hour > open_hour)
);

-- Extra days the Admin counts as a holiday. Saturdays and Sundays are always holidays and are not stored.
CREATE TABLE holidays (
    holiday_date date PRIMARY KEY,
    note         text
);

-- Price Rule: the hourly Court Rental price for [start_hour, end_hour) on one Day Type.
-- court_id NULL = every Court; a rule for one Court beats a Club-wide rule.
CREATE TABLE price_rules (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    court_id       uuid REFERENCES courts(id) ON DELETE CASCADE,
    day_type       text NOT NULL CHECK (day_type IN ('weekday','holiday')),
    start_hour     smallint NOT NULL CHECK (start_hour BETWEEN 0 AND 23),
    end_hour       smallint NOT NULL CHECK (end_hour BETWEEN 1 AND 24),
    price_per_hour numeric(10,2) NOT NULL CHECK (price_per_hour >= 0),
    label          text,
    CHECK (end_hour > start_hour)
);
CREATE INDEX ix_price_rules_court ON price_rules (court_id);

-- ---------------------------------------------------------------- bookings
CREATE SEQUENCE booking_code_seq START 10001;

-- A Booking is one Court Rental: one Court, one run of consecutive Slots.
CREATE TABLE bookings (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code            text NOT NULL UNIQUE DEFAULT ('PB' || nextval('booking_code_seq')),
    source          text NOT NULL DEFAULT 'online' CHECK (source IN ('online','walk_in')),
    user_id         uuid REFERENCES users(id),       -- the Customer; NULL for a Walk-in Booking
    guest_name      text,                            -- the Walk-in Guest
    guest_phone     text,
    court_id        uuid NOT NULL REFERENCES courts(id),
    start_at        timestamptz NOT NULL,
    end_at          timestamptz NOT NULL,
    time_range      tstzrange GENERATED ALWAYS AS (tstzrange(start_at, end_at, '[)')) STORED,
    status          text NOT NULL DEFAULT 'held'
                    CHECK (status IN ('held','confirmed','checked_in','completed','expired','cancelled','no_show')),
    total           numeric(10,2) NOT NULL CHECK (total >= 0),
    hold_expires_at timestamptz,                     -- Hold: set while status = 'held'
    counter_payment text CHECK (counter_payment IN ('cash','transfer')),   -- how a Walk-in Guest paid
    rescheduled_at  timestamptz,                     -- set by the Customer's one Reschedule
    checked_in_at   timestamptz,
    reminder_sent_at timestamptz,                    -- the "you play tomorrow" email
    cancelled_at    timestamptz,                     -- Admin Cancellation
    cancelled_by    uuid REFERENCES users(id),
    refund_note     text,                            -- how the Club refunded outside the system
    created_by      uuid REFERENCES users(id),       -- the Admin who entered a Walk-in Booking
    idempotency_key text,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CHECK (end_at > start_at),
    CHECK (
        (source = 'online'  AND user_id IS NOT NULL)
     OR (source = 'walk_in' AND user_id IS NULL AND guest_name IS NOT NULL AND guest_phone IS NOT NULL)
    ),
    UNIQUE (user_id, idempotency_key)
);
CREATE INDEX ix_bookings_user ON bookings (user_id, created_at DESC);
CREATE INDEX ix_bookings_court_time ON bookings (court_id, start_at);
CREATE INDEX ix_bookings_hold ON bookings (hold_expires_at) WHERE status = 'held';

-- The core guarantee: no two live Bookings on the same Court may overlap.
-- A Reschedule or an Admin Move is one UPDATE of court_id/start_at/end_at, checked by this same constraint.
ALTER TABLE bookings ADD CONSTRAINT no_overlapping_bookings
    EXCLUDE USING gist (court_id WITH =, time_range WITH &&)
    WHERE (status IN ('held','confirmed','checked_in'));

-- A Customer has at most one unpaid Hold at a time (closes the check-then-insert race).
CREATE UNIQUE INDEX ux_bookings_one_hold_per_user ON bookings (user_id) WHERE status = 'held';

-- Online payments. A Booking is confirmed only when the provider's webhook reports one of its payments as succeeded.
CREATE TABLE payments (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id    uuid NOT NULL REFERENCES bookings(id),
    provider      text NOT NULL,                 -- 'mock' | 'beam'
    provider_ref  text NOT NULL,                 -- the provider's own id for the charge
    method        text NOT NULL CHECK (method IN ('promptpay','card')),
    amount        numeric(10,2) NOT NULL CHECK (amount >= 0),
    status        text NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','succeeded','failed')),
    qr_payload    text,                          -- PromptPay: the text inside the QR the Customer scans
    checkout_url  text,                          -- card: the provider's hosted payment page
    expires_at    timestamptz,
    paid_at       timestamptz,
    -- Money arrived but the Booking could not be confirmed (the Hold had expired and the Court was taken, or it was
    -- already paid). The system never sends money back: the Club refunds by hand and records it here.
    refund_due    boolean NOT NULL DEFAULT false,
    refund_reason text,
    refunded_at   timestamptz,
    refunded_by   uuid REFERENCES users(id),
    refund_note   text,
    raw_webhook   jsonb,
    created_at    timestamptz NOT NULL DEFAULT now(),
    UNIQUE (provider, provider_ref)
);
CREATE INDEX ix_payments_booking ON payments (booking_id);
CREATE INDEX ix_payments_refund_due ON payments (created_at) WHERE refund_due AND refunded_at IS NULL;

-- Court Block: a period the Admin takes a Court out of sale.
CREATE TABLE court_blocks (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    court_id   uuid NOT NULL REFERENCES courts(id) ON DELETE CASCADE,
    start_at   timestamptz NOT NULL,
    end_at     timestamptz NOT NULL,
    time_range tstzrange GENERATED ALWAYS AS (tstzrange(start_at, end_at, '[)')) STORED,
    reason     text NOT NULL,
    created_by uuid REFERENCES users(id),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK (end_at > start_at)
);
CREATE INDEX ix_court_blocks_range ON court_blocks USING gist (court_id, time_range);
