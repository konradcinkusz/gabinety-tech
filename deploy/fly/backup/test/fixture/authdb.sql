-- SYNTHETIC fixture (not authservice's real schema, which is upstream's): every id/email is demo-/example.invalid. T-10.05 round trip only.
CREATE TABLE users (id text PRIMARY KEY, email text NOT NULL UNIQUE, created_at timestamptz NOT NULL);
CREATE TABLE refresh_tokens (id bigserial PRIMARY KEY, user_id text NOT NULL REFERENCES users(id) ON DELETE CASCADE, token_hash text NOT NULL, expires_at timestamptz NOT NULL);
INSERT INTO users SELECT 'demo-user-' || n, 'demo-user-' || n || '@example.invalid', timestamptz '2026-11-01 08:00+00' + n * interval '1 minute' FROM generate_series(1, 30) n;
INSERT INTO refresh_tokens (user_id, token_hash, expires_at) SELECT 'demo-user-' || (1 + n % 30), md5('demo-token-' || n), timestamptz '2026-12-01 00:00+00' FROM generate_series(1, 60) n;
