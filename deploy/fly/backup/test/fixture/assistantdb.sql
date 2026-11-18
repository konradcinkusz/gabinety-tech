-- SYNTHETIC fixture (the Assistant's call log; no free text). T-10.05 round trip only.
CREATE TABLE assistant_calls (id bigserial PRIMARY KEY, feature varchar(30) NOT NULL, outcome varchar(20) NOT NULL, cost_micro_usd bigint NOT NULL CHECK (cost_micro_usd >= 0), at timestamptz NOT NULL);
INSERT INTO assistant_calls (feature, outcome, cost_micro_usd, at) SELECT (ARRAY['brief','triage'])[1 + n % 2], 'ok', 1200 + n, timestamptz '2026-11-10 10:00+00' + n * interval '1 minute' FROM generate_series(1, 50) n;
