# Restore log

Each restore performed for T-10.05 (`deploy/fly/backup/`), newest first. A restore is **performed** only when a real dump from the real object store was restored by `restore-check.sh`. Entry 1 below is a local rehearsal on synthetic data: it proves the scripts work and can fail, it does not prove the staging backups work.

## Status

| Restore | Performed? |
|---|---|
| Staging dumps of the five databases from the real bucket into a scratch database | **Not performed.** Blocked: no Fly account or app (T-09.01), no object-store bucket or key pair (owner), T-09.08 (the first real booking, so the databases hold real rows) not done. Run it as RUNBOOK section 2 says and add it as a new entry 1 above the rehearsal. |
| Local rehearsal on synthetic data (below) | Performed 2026-11-18 |

## 1. 2026-11-18, local rehearsal, synthetic fixture

- **Environment:** PostgreSQL client 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1), server 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1), rclone v1.60.1-DEV, Linux 6.18.44-fc-v595, 2026-11-18T09:14Z. A throw-away PostgreSQL started by `deploy/fly/backup/test/local-roundtrip.sh` (127.0.0.1, password auth, random password, deleted afterwards). Object store: rclone **local-disk** backend, not S3. No Docker, no Fly.
- **Data:** the synthetic fixtures `deploy/fly/backup/test/fixture/*.sql` (ids `demo-...`, addresses `example.invalid`). They are hand-written subsets of the schemas, **not** the EF migrations' output and **not** the demo seeder's data: no .NET SDK was available in this session. The `bookings_no_overlap` constraint in the fixture is copied verbatim from `T-04.04_Bookings.cs`.
- **Command:** `deploy/fly/backup/test/local-roundtrip.sh` (it ran `backup.sh`, then `restore-check.sh <db> --latest` for each database, two negative tests, and the retention test).
- **Result:** exit status 0, "ROUND TRIP: all checks passed".

| Database | Dump size (bytes) | Table = rows, source and restored equal | Total | Constraint types (count) |
|---|---|---|---|---|
| `authdb` | 6214 | `refresh_tokens`=60, `users`=30 | 90 | p=2 f=1 u=1 |
| `directorydb` | 10034 | `offerings`=40, `practitioners`=20, `verification_events`=25 | 85 | p=3 f=2 u=2 c=3 |
| `bookingdb` | 21979 | `bookings`=120, `ledger_entries`=60, `payments`=60, `webhook_events`=50 | 290 | p=4 f=2 u=1 c=1 **x=1** (`bookings_no_overlap`) |
| `reputationdb` | 9494 | `moderation_events`=60, `rating_summaries`=10, `reviews`=40 | 110 | p=3 f=1 u=1 c=1 |
| `assistantdb` | 3492 | `assistant_calls`=50 | 50 | p=1 c=1 |

For every database, all five checks passed: `pg_restore --exit-on-error` exit status 0; row counts of every table equal to the counts taken in the same snapshot as the dump; constraint counts per type equal; no `NOT VALID` constraint; every foreign key free of orphans (authdb 1, directorydb 2, bookingdb 2, reputationdb 1, assistantdb 0 foreign keys). For `bookingdb` also: `bookings_no_overlap` present as `EXCLUDE USING gist`.

**The checks can fail** (both ran in the same script and were rejected, exit status non-zero):
1. a counts sidecar with one count changed to 999999 -> `FAIL row counts differ`;
2. a `bookingdb` dump taken after `ALTER TABLE bookings DROP CONSTRAINT bookings_no_overlap` -> `FAIL constraint counts differ` and `FAIL exclusion constraint bookings_no_overlap is missing`.

**Retention** (same script, rclone local remote): 18 daily sets (2026-11-01 to 2026-11-18) plus one orphan dump from 2026-10-10, reference time 2026-11-18 13:00 UTC. Dry run listed 19 files to delete and deleted nothing; `APPLY=1` left the dumps of 2026-11-01, 11-08, 11-12, 11-13, 11-14, 11-15, 11-16, 11-17, 11-18: the 7 newest days plus the newest set of each of the ISO weeks 47, 46, 45, 44 (the days 11-18, 11-15, 11-08, 11-01).

**Not shown by this entry:** a restore of a dump of a PostgreSQL 17 server (the Fly image) with a 17 client; a restore from S3; the scheduled machine running; any timing or size of a real database; encryption of the dump; the time a restore of real data takes (record it at the first real restore).

<details><summary>Full output of the run (rclone and psql noise removed; no credential is printed by the scripts)</summary>

```
environment: PostgreSQL client 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1), server 16.15 (Ubuntu 16.15-0ubuntu0.24.04.1), rclone v1.60.1-DEV, Linux 6.18.44-fc-v595, 2026-11-18T09:14Z
== the database list of backup.sh equals init-databases.sql
PASS  assistantdb authdb bookingdb directorydb reputationdb 
== load the synthetic fixtures (init-databases.sql, then one fixture per database)
== backup.sh (rclone local-disk remote, not S3)
2026-11-18T09:14:16Z backup ok authdb authdb-20261118T091416Z dump_bytes=6214 tables=2
2026-11-18T09:14:17Z backup ok directorydb directorydb-20261118T091416Z dump_bytes=10034 tables=3
2026-11-18T09:14:17Z backup ok bookingdb bookingdb-20261118T091417Z dump_bytes=21980 tables=4
2026-11-18T09:14:17Z backup ok reputationdb reputationdb-20261118T091417Z dump_bytes=9494 tables=3
2026-11-18T09:14:17Z backup ok assistantdb assistantdb-20261118T091417Z dump_bytes=3492 tables=1
2026-11-18T09:14:17Z backup done: all databases backed up
== restore-check.sh --latest for each database
--- authdb
set: authdb-20261118T091416Z
scratch database: restore_scratch_authdb_1794993257  (dump: 6214 bytes)
PASS  pg_restore exit status 0
PASS  row counts of all 2 tables equal the source (90 rows): public.refresh_tokens=60 public.users=30 
PASS  constraint counts per type equal the source (f=1 p=2 u=1 )
PASS  no constraint is NOT VALID
PASS  all 1 foreign keys have no orphan row
----
RESULT: restore of authdb verified
--- directorydb
set: directorydb-20261118T091416Z
scratch database: restore_scratch_directorydb_1794993258  (dump: 10034 bytes)
PASS  pg_restore exit status 0
PASS  row counts of all 3 tables equal the source (85 rows): public.offerings=40 public.practitioners=20 public.verification_events=25 
PASS  constraint counts per type equal the source (c=3 f=2 p=3 u=2 )
PASS  no constraint is NOT VALID
PASS  all 2 foreign keys have no orphan row
----
RESULT: restore of directorydb verified
--- bookingdb
set: bookingdb-20261118T091417Z
scratch database: restore_scratch_bookingdb_1794993259  (dump: 21980 bytes)
PASS  pg_restore exit status 0
PASS  row counts of all 4 tables equal the source (290 rows): public.bookings=120 public.ledger_entries=60 public.payments=60 public.webhook_events=50 
PASS  constraint counts per type equal the source (c=1 f=2 p=4 u=1 x=1 )
PASS  no constraint is NOT VALID
PASS  all 2 foreign keys have no orphan row
PASS  exclusion constraint bookings_no_overlap present (EXCLUDE USING gist (practitioner_id WITH =, tsrange((start_u...)
----
RESULT: restore of bookingdb verified
--- reputationdb
set: reputationdb-20261118T091417Z
scratch database: restore_scratch_reputationdb_1794993259  (dump: 9494 bytes)
PASS  pg_restore exit status 0
PASS  row counts of all 3 tables equal the source (110 rows): public.moderation_events=60 public.rating_summaries=10 public.reviews=40 
PASS  constraint counts per type equal the source (c=1 f=1 p=3 u=1 )
PASS  no constraint is NOT VALID
PASS  all 1 foreign keys have no orphan row
----
RESULT: restore of reputationdb verified
--- assistantdb
set: assistantdb-20261118T091417Z
scratch database: restore_scratch_assistantdb_1794993260  (dump: 3492 bytes)
PASS  pg_restore exit status 0
PASS  row counts of all 1 tables equal the source (50 rows): public.assistant_calls=50 
PASS  constraint counts per type equal the source (c=1 p=1 )
PASS  no constraint is NOT VALID
PASS  all 0 foreign keys have no orphan row
----
RESULT: restore of assistantdb verified
== negative test 1: a tampered counts sidecar must FAIL
      FAIL  row counts differ (< source, > restored)
      RESULT: restore of directorydb FAILED
PASS  rejected
== negative test 2: a bookingdb dump WITHOUT bookings_no_overlap must FAIL
      FAIL  constraint counts differ (< source, > restored)
      FAIL  exclusion constraint bookings_no_overlap is missing
      RESULT: restore of bookingdb FAILED
PASS  rejected
== retention.sh: dry run, then apply, on a remote that holds 18 daily sets plus an old orphan dump
      == bookingdb: keep 9 dump(s), delete 19 file(s) (dry run, set APPLY=1 to delete)
      DELETE bookingdb-20261010T020000Z.dump
      DELETE bookingdb-20261102T020000Z.counts.tsv
      DELETE bookingdb-20261102T020000Z.dump
      files before: 37
      kept dumps after apply: 20261101 20261108 20261112 20261113 20261114 20261115 20261116 20261117 20261118 
PASS  retention kept exactly 7 daily + the newest set of each of 4 ISO weeks; the old orphan was deleted
ROUND TRIP: all checks passed
```

</details>
