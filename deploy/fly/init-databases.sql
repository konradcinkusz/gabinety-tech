-- The five databases of SPEC 4.1/4.3 on one PostgreSQL instance (T-09.04). Idempotent: safe to run on first start and by hand later.
--   psql -h gabinety-postgres-staging.internal -U postgres -f deploy/fly/init-databases.sql
-- Schemas are created by each service / authservice itself on start; this file creates only the empty databases. No passwords here.
SELECT format('CREATE DATABASE %I', d)
FROM (VALUES ('authdb'), ('directorydb'), ('bookingdb'), ('reputationdb'), ('assistantdb')) AS t(d)
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = t.d)
\gexec
