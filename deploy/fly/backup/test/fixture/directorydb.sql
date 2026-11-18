-- SYNTHETIC fixture shaped after CONTRACTS 3 (a subset of columns). T-10.05 round trip only.
CREATE TABLE practitioners (id uuid PRIMARY KEY, user_id varchar(64) NOT NULL UNIQUE, slug varchar(60) NOT NULL UNIQUE, display_name varchar(80) NOT NULL,
  status text NOT NULL CHECK (status IN ('Draft','Submitted','Approved','Rejected','Suspended')), rating_count int NOT NULL DEFAULT 0, rating_avg numeric(3,2) NOT NULL DEFAULT 0);
CREATE TABLE offerings (id uuid PRIMARY KEY, practitioner_id uuid NOT NULL REFERENCES practitioners(id), title varchar(120) NOT NULL, duration_minutes smallint NOT NULL CHECK (duration_minutes BETWEEN 15 AND 240), price_minor bigint NOT NULL CHECK (price_minor >= 0));
CREATE TABLE verification_events (id bigserial PRIMARY KEY, practitioner_id uuid NOT NULL REFERENCES practitioners(id), kind text NOT NULL, at timestamptz NOT NULL);
INSERT INTO practitioners SELECT md5('demo-p-' || n)::uuid, 'demo-prac-user-' || n, 'demo-prac-' || n, 'Demo Practitioner ' || n, (ARRAY['Approved','Approved','Submitted','Suspended'])[1 + n % 4], n % 7, 4.5 FROM generate_series(1, 20) n;
INSERT INTO offerings SELECT md5('demo-o-' || n)::uuid, md5('demo-p-' || (1 + n % 20))::uuid, 'Demo offering ' || n, 30 + 30 * (n % 3), 5000 + 1000 * n FROM generate_series(1, 40) n;
INSERT INTO verification_events (practitioner_id, kind, at) SELECT md5('demo-p-' || (1 + n % 20))::uuid, 'Submitted', timestamptz '2026-11-02 09:00+00' + n * interval '1 hour' FROM generate_series(1, 25) n;
