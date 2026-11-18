# Backups of the five databases (T-10.05)

Daily `pg_dump` of every database of `../init-databases.sql` to an S3-compatible object store, a retention rule, and a restore check. Everything here was exercised **locally** against a throw-away PostgreSQL 16 and an rclone *local-disk* remote (`docs/operations/RESTORE-LOG.md`); nothing ran on Fly or against a real bucket (no account, no bucket: T-09.01 is the owner's).

| File | What it does |
|---|---|
| `backup.sh` | For each database: `pg_dump -Fc --compress=6` plus a per-table row-count sidecar (`<db>-<UTC ts>.counts.tsv`), both taken in one `REPEATABLE READ` snapshot (`pg_export_snapshot`), uploaded with `rclone copyto` (sidecar last), then `rclone check`. Exit 0 only if every database succeeded. |
| `retention.sh` | Keeps the newest complete set of each of the 7 most recent UTC days **and** of each of the 4 most recent ISO weeks (the current week counts); deletes the rest. Dry run unless `APPLY=1`. Never touches a file it does not recognise; keeps an incomplete set younger than 24 h. |
| `restore-check.sh` | Restores one dump into `restore_scratch_<db>_<epoch>` and checks: `pg_restore` exit status, the row count of every table against the sidecar, constraint counts per type, no `NOT VALID` constraint, an orphan anti-join per foreign key, and for `bookingdb` the `bookings_no_overlap` exclusion constraint. Drops the scratch database at the end. |
| `run-daily.sh`, `Dockerfile`, `backup.fly.toml` | The scheduled-machine template (`postgres:17-alpine` + `rclone`). **Template only.** |
| `test/local-roundtrip.sh`, `test/fixture/*.sql` | The local round trip, two negative tests and the retention test. The fixtures are **synthetic** (`demo-` ids, `example.invalid` addresses, hand-written subsets of the schemas); they are not the EF migrations' schema. |

## Configuration (environment only; nothing is passed as an argument or printed)

| Name | Kind | Meaning |
|---|---|---|
| `PGHOST` `PGPORT` `PGUSER` | plain | the instance, `gabinety-postgres-staging.internal:5432`, `postgres` |
| `PGPASSWORD` | **secret** | the `POSTGRES_PASSWORD` of the PostgreSQL app |
| `BACKUP_REMOTE` | plain | rclone path `backup:<BUCKET>/staging` |
| `RCLONE_CONFIG_BACKUP_TYPE` `_PROVIDER` `_ENDPOINT` `_REGION` | plain | `s3`, `Other`, `<S3_ENDPOINT>`, `<REGION>` |
| `RCLONE_CONFIG_BACKUP_ACCESS_KEY_ID` `_SECRET_ACCESS_KEY` | **secret** | the bucket's key pair; give it write and list on the one bucket, no more |
| `KEEP_DAILY` `KEEP_WEEKLY` | plain | 7 and 4 |
| `BACKUP_DATABASES` | plain, optional | default: the five of `init-databases.sql`; the test checks the default equals that file |

Object layout: `<BACKUP_REMOTE>/<db>/<db>-<YYYYMMDDTHHMMSSZ>.dump` and `.counts.tsv`. A dump without its `.counts.tsv` is incomplete and is never used by `restore-check.sh --latest`.

## Not verified (read before relying on it)

- Not run on Fly: the `Dockerfile` was not built, `backup.fly.toml` was not validated, the `fly machine run --schedule daily` flags are from documentation, the private-network connection to `gabinety-postgres-staging.internal` was not made.
- Not run against S3: only rclone's `local` backend. `rclone check` against S3 may compare size only when the store has no common hash for multipart uploads; check once with the real bucket.
- `postgres:17-alpine` has a `pg_dump` 17 client; the local run used 16 against 16. A dump of a 17 server needs a 17 client.
- The fixtures are small (a few hundred rows, a few kilobytes): no timing or size figure here says anything about real data.
- No encryption of dumps beyond what the bucket provides. The dumps hold personal data and brief/review text; **owner decision needed** (server-side encryption, or `rclone crypt`) before the first real dump (RUNBOOK section 11).
