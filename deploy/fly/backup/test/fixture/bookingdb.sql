-- SYNTHETIC fixture: a SUBSET of the bookings columns plus the exclusion constraint copied verbatim from the migration T-04.04_Bookings.cs.
-- It is hand-written, NOT produced by the EF migrations (no .NET SDK was available). T-10.05 round trip only.
CREATE EXTENSION IF NOT EXISTS btree_gist;
CREATE TABLE bookings (id uuid PRIMARY KEY, client_user_id varchar(64) NOT NULL, practitioner_id uuid NOT NULL, practitioner_user_id varchar(64) NOT NULL,
  price_minor bigint NOT NULL, start_utc timestamptz NOT NULL, end_utc timestamptz NOT NULL, status varchar(30) NOT NULL, brief_text varchar(2000) NOT NULL,
  idempotency_key uuid NOT NULL, created_at timestamptz NOT NULL);
CREATE UNIQUE INDEX "IX_bookings_client_user_id_idempotency_key" ON bookings (client_user_id, idempotency_key);
ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
  EXCLUDE USING gist (
    practitioner_id WITH =,
    tsrange(start_utc AT TIME ZONE 'UTC', (end_utc AT TIME ZONE 'UTC') + interval '15 minutes') WITH &&)
  WHERE (status IN ('PendingPayment','Confirmed','Disputed'));
CREATE TABLE payments (id uuid PRIMARY KEY, booking_id uuid NOT NULL REFERENCES bookings(id), provider varchar(30) NOT NULL, provider_payment_id varchar(100) NOT NULL, amount_minor bigint NOT NULL CHECK (amount_minor >= 0), status varchar(20) NOT NULL, UNIQUE (provider, provider_payment_id));
CREATE TABLE ledger_entries (id uuid PRIMARY KEY, booking_id uuid NOT NULL REFERENCES bookings(id), kind varchar(30) NOT NULL, amount_minor bigint NOT NULL, pair_key varchar(120));
CREATE TABLE webhook_events (provider varchar(30) NOT NULL, event_id varchar(100) NOT NULL, outcome varchar(20) NOT NULL, PRIMARY KEY (provider, event_id));
-- 10 practitioners x 12 half-hour bookings, one per hour: the 15 minute buffer never overlaps.
INSERT INTO bookings SELECT md5('demo-b-' || p || '-' || s)::uuid, 'demo-client-' || (1 + (p + s) % 15), md5('demo-p-' || p)::uuid, 'demo-prac-user-' || p, 9000,
  timestamptz '2026-12-01 08:00+00' + s * interval '1 hour', timestamptz '2026-12-01 08:30+00' + s * interval '1 hour',
  (ARRAY['Confirmed','Completed','PendingPayment','CancelledByClient'])[1 + (p + s) % 4], 'demo brief ' || p || '-' || s, md5('demo-k-' || p || '-' || s)::uuid, now()
FROM generate_series(1, 10) p, generate_series(0, 11) s;
INSERT INTO payments SELECT md5('demo-pay-' || id)::uuid, id, 'mock', 'mock_' || md5(id::text), price_minor, 'Succeeded' FROM bookings WHERE status IN ('Confirmed','Completed');
INSERT INTO ledger_entries SELECT md5('demo-led-' || id)::uuid, id, 'ClientCharge', price_minor, NULL FROM bookings WHERE status IN ('Confirmed','Completed');
INSERT INTO webhook_events SELECT 'mock', 'evt_demo_' || n, 'Applied' FROM generate_series(1, 50) n;
