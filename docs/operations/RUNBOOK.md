# Runbook (T-10.05)

Owner's operating procedures for staging on Fly.io. Written from the code of this repository at `f20b908`, the spec (`SPEC` §4.3, §8, §11, `CONTRACTS`), `AUTH.md` and `deploy/fly/`. Nothing here is secret; `<placeholders>` are filled by the owner.

## 1. How to read this document

**Every step carries a tag.** `[run: where, when]` means a person or an agent did exactly that step and saw its result. `[not run: reason]` means nobody did; the step is derived from code and documentation and is **unverified**. A `[not run]` step may be wrong in a flag, a path or an assumption about Fly; the first person to run it corrects this file and changes the tag.

State when this was written (2026-11-18): no Fly account or app exists (T-09.01, T-09.02, T-09.03 not done), the payment provider is the mock (T-09.05), no real booking has happened on staging (T-09.08), and the authoring session had no .NET SDK, so no service was started. Therefore only the backup, restore and retention steps of section 2 were run, and only locally on synthetic data. Everything else is `[not run]`.

Names: apps are `gabinety-<name>-staging` (`web`, `directory`, `booking`, `reputation`, `assistant`, `authservice`, `postgres`, `backup`); services listen on 8080 on the private network (`<app>.internal:8080`); only `web`, `authservice` and Booking's webhook path are public (SPEC §4.3). `fly` is the Fly CLI; every command takes `-a <app>`.

## 2. Backups and restore

Scripts, configuration and the "not verified" list: `deploy/fly/backup/README.md`. Evidence: [`RESTORE-LOG.md`](RESTORE-LOG.md).

| # | Step | Tag |
|---|---|---|
| 2.1 | Back up all five databases: `deploy/fly/backup/backup.sh` (dump + count sidecar in one snapshot, upload, verify). | `[run: local PostgreSQL 16 on synthetic fixture and an rclone local-disk remote, 2026-11-18, RESTORE-LOG entry 1]` The Fly scheduled machine and the S3 upload: `[not run: no Fly account, no bucket (T-09.01, owner)]` |
| 2.2 | Create the app, secrets and the daily schedule: the commands at the top of `deploy/fly/backup/backup.fly.toml`. | `[not run: no Fly account (T-09.01)]` |
| 2.3 | Apply the retention rule (7 daily + 4 weekly): `APPLY=1 deploy/fly/backup/retention.sh` (dry run without `APPLY=1`); `run-daily.sh` does it after each successful backup. | `[run: local rclone remote, 2026-11-18, RESTORE-LOG entry 1]` On the real bucket: `[not run: no bucket]` |
| 2.4 | **Monthly, and after any change to the schema or the scripts: restore the newest set of each database into a scratch database and check it.** `PGHOST=<a PostgreSQL that is not production if you have one> deploy/fly/backup/restore-check.sh <db> --latest`. It prints PASS/FAIL per check and exits non-zero on any failure. Record the output in `RESTORE-LOG.md`. | `[run: local, synthetic data, 2026-11-18]` Real dumps: `[not run: no dumps exist (T-09.08 not done)]` |
| 2.5 | **Real disaster restore** (the database is lost or damaged): (a) stop the writers: `fly scale count 0 -a gabinety-<svc>-staging` for the affected service; (b) run 2.4 first against the set you intend to use, and only continue when it says `verified`; (c) `pg_restore --clean --if-exists --no-owner -d <db>` of the same dump into the real database over `fly proxy 15432:5432 -a gabinety-postgres-staging`; (d) start the writers, check `/health`; (e) expect to lose everything written since the dump (up to 24 h) and reconcile payments with the provider's dashboard. | `[not run: no instance, no dump]` Note that (c) is a plain `pg_restore` and was only exercised into scratch databases. |
| 2.6 | Dumps hold personal data. Decide encryption at rest for the bucket before the first real dump (section 11, question 1). | `[not run: owner decision pending]` |

## 3. Deploy

The deploy procedure belongs to `docs/operations/DEPLOY.md` (T-09.03). That file does not exist on `main` yet: **pending T-09.03**. Until then, the order is SPEC §9: postgres, authservice, directory, booking, reputation, assistant, web, started by a tag `v*`.

| # | Step | Tag |
|---|---|---|
| 3.1 | First deploy of PostgreSQL and authservice: `docs/operations/AUTH.md` sections 2 to 6. | `[not run: no Fly account (T-09.01)]` |
| 3.2 | Deploy of the backends and the web app, and the post-deploy checks (`scripts/assert-jwks-one-key.sh`, `/health`). | `[not run: pending T-09.03, DEPLOY.md does not exist]` |
| 3.3 | After every deploy, read `fly status -a <app>` and `fly logs -a <app>` for the services deployed: the migrations run after start (P4, `MigrationHostedService`); a service that cannot migrate stays unhealthy. | `[not run: no deployed app]` |

## 4. Rollback

Code and schema roll back differently. Booking and the other services apply EF migrations on start and a release is not guaranteed to run against the previous schema, so **rolling back code across a migration is not safe by default**; read the migration of the release first (`src/Gabinety.<Svc>/Persistence/Migrations/`).

| # | Step | Tag |
|---|---|---|
| 4.1 | List releases and their images: `fly releases -a <app> --image`. | `[not run: no app]` |
| 4.2 | Roll the code back (no schema change in the bad release): `fly deploy -a <app> --image <previous image reference>`. Then `fly status -a <app>` and `/health`. | `[not run: no app]` |
| 4.3 | The bad release contained a migration: either roll forward with a fix (preferred), or restore the database from the last good dump (section 2.5) and then deploy the previous image. Never edit a migration that has been applied. | `[not run: no app, no real migration history on staging]` |
| 4.4 | Roll back authservice: its image is upstream's, pinned by digest (`authservice.fly.toml`); redeploy the previous digest. A signing-key rollback is section 5.1. | `[not run: no app]` |

## 5. Rotate secrets

SPEC §11 names these platform secrets: the webhook secret, the internal keys, the model API keys and the e-mail key; `AUTH.md` adds the authservice signing key, the PostgreSQL password and the GitHub OAuth secret; this runbook adds the backup credentials and the Fly deploy token. A secret is set with `fly secrets set --stage -a <app> NAME=<value>` and takes effect at the next deploy (`fly secrets deploy -a <app>`, or `fly deploy`); without `--stage` Fly restarts the machines at once. Generate values with `openssl rand -base64 33` and keep them in the owner's password manager; never in a chat, a file in the repository or a shell history (`read -rs VAR` then `fly secrets set ... "NAME=$VAR"`).

| # | Secret (app: name) | Procedure | Tag |
|---|---|---|---|
| 5.1 | authservice signing key (`authservice: Jwt__PrivateKeyPem`, `Jwt__PreviousPublicKeyPem`) | `scripts/gen-authservice-key.sh <out> --fly gabinety-authservice-staging` writes a new key. For a rotation without logging everybody out: new key as `Jwt__PrivateKeyPem`, the old **public** key as `Jwt__PreviousPublicKeyPem` (`AUTH.md` section 4): JWKS then has two keys, so run `EXPECTED_KEYS=2` by hand; remove `Jwt__PreviousPublicKeyPem` after the longest access-token lifetime. Services refetch the JWKS; check a login and a Directory call. | `[not run: no authservice app]` |
| 5.2 | PostgreSQL password (`postgres: POSTGRES_PASSWORD`; in each connection string: `authservice: ConnectionStrings__DefaultConnection`, each backend `ConnectionStrings__<db>`; `backup: PGPASSWORD`) | The image reads `POSTGRES_PASSWORD` only on first start, so changing the secret does **not** change the database role. Run `ALTER USER postgres PASSWORD '<new>'` over `fly proxy 15432:5432 -a gabinety-postgres-staging` (read the password with `read -rs`, pass it with `\password postgres` so it is not in history), then stage all the secrets above and deploy every service and the backup image. Expect a short outage: services reconnect on the next deploy. | `[not run: no instance]` |
| 5.3 | Internal keys (`Internal__Key` on each **callee**: `directory`, `booking`, `reputation`, `assistant`; `Internal__Keys__<Callee>` on each **caller**). Callers by the code: Booking uses `Internal__Keys__Directory`; Reputation uses `Internal__Keys__Directory`, `Internal__Keys__Booking`, `Internal__Keys__Assistant`. Re-run `grep -rn "Internal:Keys:" src` before rotating: this list is the grep of 2026-11-18. | The callee compares one value (`InternalKeyHandler`, `src/Gabinety.ServiceDefaults/Security.cs`): there is **no overlap period**. Stage the new value on the callee and on every caller, then deploy the callee and its callers one after the other, callee first is acceptable. During the gap internal calls answer 401: new bookings are refused with a dependency error (Booking asks Directory in-request), and the push jobs fail and retry on their next 60 s cycle. Use one distinct key per callee (the AppHost uses one shared value only locally). The erasure hooks (section 8) use the same keys. | `[not run: no apps]` |
| 5.4 | Payment webhook secret (`booking: Payments__WebhookSecret`; the provider's dashboard holds the same value) | Change both sides together: stage on Booking, change in the provider's dashboard, deploy Booking. Events signed with the old secret in between get 401 and the provider retries them (section 6); events are idempotent by event id. Mock payments run only in the fake profile and Booking refuses to start with them anywhere else (F-01), so there is nothing to rotate until the real provider adapter exists (T-09.05). | `[not run: no real provider, mock only]` |
| 5.5 | Model API keys (`assistant: Llm__CustomModels__<N>__ApiKey`, one per bring-your-own-key model, `COPILOT-SDK` §6) | Create a new key at the model vendor, `fly secrets set --stage -a gabinety-assistant-staging "Llm__CustomModels__<N>__ApiKey=<new>"`, deploy, check one brief on staging, revoke the old key at the vendor. The Assistant degrades to the plain form while it is down (P8), so this is not user-visible. | `[not run: no app, no vendor key yet (T-02.06 owner)]` |
| 5.6 | E-mail key (`booking`, `Notifications__Provider=Smtp`) | **The configuration key names of the SMTP credentials are not defined in this repository's code** (only `Notifications:Provider` is read): section 11, question 3. Until they exist the procedure cannot be written. | `[not run: names undefined in code]` |
| 5.7 | GitHub OAuth secret (`authservice: OAuth__GitHub__ClientSecret`; optional, `AUTH.md` section 5) | Generate a new client secret at GitHub, stage, deploy authservice, delete the old secret at GitHub. | `[not run: no app, no OAuth app]` |
| 5.8 | Backup credentials (`backup: RCLONE_CONFIG_BACKUP_ACCESS_KEY_ID`, `..._SECRET_ACCESS_KEY`) | New key pair at the object-store provider, stage, `fly secrets deploy -a gabinety-backup-staging`, run the next backup by hand (`fly machine start <id>`) and check the log, revoke the old pair. | `[not run: no bucket]` |
| 5.9 | Fly deploy token (GitHub Actions secret) | `fly tokens create deploy -a <app>`, update the repository secret, delete the old token. | `[not run: no Fly account]` |
| 5.10 | A secret leaked (it appeared in a log, a chat or a commit) | Rotate it first (the row above), then remove the leak; the secret scanner (`secret-scan` job) catches commits, not chats. | `[not run]` |

## 6. Payment webhook failure burst

`POST /webhooks/payments/{provider}` on Booking, anonymous, the only public path besides the web app. What the code does (`PaymentWebhookFeature`, `PaymentEventHandler`):

| Symptom | Cause by the code | Log line (service log of Booking) |
|---|---|---|
| 401, many | no secret configured (every deployed provider is not Mock, so a missing `Payments__WebhookSecret` rejects all of them), or the signature does not verify | Warning `Webhook for {Provider} rejected: no webhook secret is configured` / `...: the signature is invalid` |
| 200 `Rejected` | `PaymentSucceeded` whose amount or currency differs from the booking | Error `ALERT payment amount or currency mismatch for booking {BookingId}: expected ..., got ...`; the event is stored with outcome `Rejected` |
| 503 `dependency_unavailable` + `Retry-After` | Directory unreachable (the meeting-link lookup before confirming) | none specific |
| 202 `RetryLater` + `Retry-After` | the payment/booking row is not committed yet | none |
| 200 `Duplicate` / `Ignored` / `Applied` | normal: the event id was seen / unknown kind / applied | none |
| 413 | body over 64 KB | none |

| # | Step | Tag |
|---|---|---|
| 6.1 | Count and classify: `fly logs -a gabinety-booking-staging \| grep -E 'Webhook for\|ALERT payment'`. Many 401s starting at one minute: a configuration change (section 5.4 or a provider change). Many 401s with no change on our side: someone is posting forged events; nothing is applied (signature first), so no money moves; raise the provider-side IP allow-list if the provider offers one. | `[not run: no app]` |
| 6.2 | If it follows a rotation: set the same secret on both sides (5.4). If Directory is down: `fly status -a gabinety-directory-staging`, restart it (`fly machine restart <id> -a ...`); the provider retries 503s on its own schedule. | `[not run: no app]` |
| 6.3 | After the cause is fixed, ask the provider to **resend the failed events** from its dashboard. Replay is safe: `webhook_events` is keyed `(provider, event_id)` and a repeat answers `Duplicate`. | `[not run: no real provider]` |
| 6.4 | Check money consistency (SPEC §8 alert "Confirmed with no payment Succeeded"), via `fly proxy 15432:5432 -a gabinety-postgres-staging` then `psql -h localhost -p 15432 -U postgres -d bookingdb`: `SELECT b.id, b.status, b.created_at FROM bookings b WHERE b.status = 'Confirmed' AND NOT EXISTS (SELECT 1 FROM payments p WHERE p.booking_id = b.id AND p.status = 'Succeeded');` Expect zero rows. A row is an incident: compare with the provider, do not edit `bookings` by hand. And `SELECT outcome, count(*) FROM webhook_events GROUP BY 1;`. | `[not run: no database; the column and status names are from migration T-04.05 and the entities, not from a live schema]` |
| 6.5 | Gap: SPEC §8 says the signature-failure alert is an `Error` log line with a stable event id; the code logs a `Warning` without one. Section 11, item 6. | `[not run]` |

## 7. A stuck job

Booking runs `hold_expiry`, `auto_complete` and `payout` as hosted services on a 60 s timer (`Jobs:IntervalSeconds`, default 60; first run one interval after start; `src/Gabinety.Booking/Jobs/Base/PeriodicJob.cs`). Booking also runs the stats push and the notification dispatcher, and Reputation runs the summary push, each on its own 60 s loop. Jobs are idempotent and safe in two processes at once, so **running a job again is always allowed**.

| # | Step | Tag |
|---|---|---|
| 7.1 | See it. Metrics (OTLP): `gabinety_job_lag_seconds{job}` (how late a cycle started; SPEC alert: over 10 minutes) and `gabinety_job_runs_total{job,outcome}` with `outcome=error`. No alerting product is wired in the MVP: read the logs: `fly logs -a gabinety-booking-staging \| grep -E 'Job .* run failed\|could not (expire\|complete\|be processed)\|Payout'`. The lines carry the job name, a booking id and the exception **type**, never a message. | `[not run: no app]` |
| 7.2 | Symptoms by user effect: holds not expiring (a `PendingPayment` booking past `hold_expires_at` still blocks the slot, `bookings_no_overlap`); `Confirmed` bookings not completing 48 h after the end; payouts not sent. Check: `SELECT status, count(*) FROM bookings WHERE (status = 'PendingPayment' AND hold_expires_at < now()) OR (status = 'Confirmed' AND end_utc < now() - interval '48 hours') GROUP BY 1;` over `fly proxy` (section 6.4). Expect zero or a few rows younger than one minute. | `[not run: no database]` |
| 7.3 | Rerun. **There is no endpoint that triggers a job**; the rerun is the next 60 s cycle. A cycle that throws is logged and counted, and the loop continues, so a failing job retries by itself. If the loop itself is dead or hung, restart the machine: `fly machine restart <id> -a gabinety-booking-staging` (the first cycle runs one interval, 60 s, after start). | `[not run: no app]` |
| 7.4 | The payout job gives up on a booking after a fixed number of failed attempts and logs Error `Payout for booking {BookingId} failed {Attempts} times and is not retried until the service restarts`: the attempt counter is in memory, so the rerun for that case **is** the restart in 7.3. Find the cause first (provider outage, wrong account), otherwise it fails again. | `[not run: no app, mock payouts only]` |
| 7.5 | To stop all jobs of a service on purpose (an incident that they make worse): `fly secrets set --stage -a <app> Jobs__Enabled=false` and deploy; the loop returns at start. Remove the secret and redeploy to resume. | `[not run: no app]` |

## 8. GDPR erasure across authservice and the three hooks

Order (SPEC §8): authservice deletes the account, then the owner calls the erasure hook of Directory, Booking and Reputation with the user's id. Ledger rows, payments, payouts, ratings and the booking's amounts, statuses and times are **kept** for accounting (assumption A-13); personal and free text is replaced.

**Get the user id first.** It is the token subject (`sub`) of the account, and every hook is keyed by it. After authservice has deleted the account you cannot ask authservice for it. Record it while the user is still known (the person's own token, or authservice's admin API: its route for looking a user up is not documented in `docs/spikes/authservice.md`, section 11 question 4).

| # | Step | Tag |
|---|---|---|
| 8.1 | authservice: the user deletes their own account with `DELETE /api/v1/auth/account`, Bearer token, JSON body `{"password": "<if the account has one>", "confirmation": "DELETE"}`, answer `200 {message}`. This is a **soft delete with a retention period before permanent deletion** (`docs/spikes/authservice.md`, row "Delete account"). An erasure requested by someone else (the owner on a written request): authservice's admin delete route is **not documented** in the spike; section 11, question 4. The retention period before the data is really gone is authservice's and unknown here. | `[not run: no authservice app; behaviour from the spike's reading of upstream source]` |
| 8.2 | Directory hook: `DELETE /internal/v1/subjects/{userId}` on `gabinety-directory-staging`, header `X-Internal-Key: <Directory's Internal__Key>`. Answer `204`, also for an unknown user. Effect (`InternalFeature.Erase`): the practitioner's display name becomes `Usunięty użytkownik`; headline, bio, GitHub login and link flags, NIP, portfolio URL, default meeting URL, status reason and the GitHub challenge fields are cleared; status becomes `Suspended`; `user_id` and `slug` become random values; every offering is emptied (title, description, meeting URL) and deactivated. | `[not run: not started, no .NET SDK, no app]` Read in code: `src/Gabinety.Directory/Features/Internal/InternalFeature.cs`. |
| 8.3 | Booking hook: `DELETE /internal/v1/subjects/{userId}` on `gabinety-booking-staging`, header `X-Internal-Key: <Booking's Internal__Key>`, answer `204`. Effect (`Features/Internal/Erasure/ErasureFeature.cs`): in every booking of the user as client or practitioner, `brief_text` becomes `[usunięto]` and `dispute_text` and `status_reason` are cleared; the user id becomes `erased-<40 hex of SHA-256>` (deterministic, one-way); as practitioner also `meeting_url_snapshot` is cleared and `offering_title` replaced; the client's `idempotency_records` and the client id inside the `pair_key` of their ledger rows get the same token. One transaction, batches of 500. Idempotent; an id that already starts with `erased-` is a no-op. | `[not run: not started, no .NET SDK, no app]` Read in code. |
| 8.4 | Reputation hook: `DELETE /internal/v1/subjects/{userId}` on `gabinety-reputation-staging`, header `X-Internal-Key: <Reputation's Internal__Key>`, answer `204`. Effect (`Features/Internal/Erasure/ErasureFeature.cs`): for every review the user wrote, text, the practitioner's reply and the moderator's note become `Opinia usunięta na wniosek autora`; the user id becomes `erased-<random>`; the notes of `Published`, `Rejected` and `Replied` events are replaced; ratings and `rating_summaries` are kept. | `[not run: not started, no .NET SDK, no app]` Read in code. |
| 8.5 | How to call a hook (the apps have no public listener, the images have no `curl`): `read -rs IK` (paste that service's `Internal__Key`, not echoed) then `fly proxy 18080:8080 -a gabinety-<svc>-staging &` then `curl -sS -o /dev/null -w '%{http_code}\n' -X DELETE -H @<(printf 'X-Internal-Key: %s' "$IK") http://localhost:18080/internal/v1/subjects/<userId>` then `unset IK`. Expect `204`. Do the three services one after the other with their own keys. | `[not run: no apps, no keys]` |
| 8.6 | Verify: re-run the same call (`204` again, idempotent); then over `fly proxy` to PostgreSQL check that the id is gone: `SELECT count(*) FROM bookings WHERE client_user_id = '<userId>' OR practitioner_user_id = '<userId>';` (bookingdb), `... FROM reviews WHERE client_user_id = '<userId>'` (reputationdb), `... FROM practitioners WHERE user_id = '<userId>'` (directorydb): all `0`. Do not paste the user id anywhere that is logged. | `[not run: no database]` |
| 8.7 | Not covered by a hook: the **Assistant** has no erasure hook (SPEC lists three). Its only table in the code is `Usage` (a usage counter, `AssistantDbContext`); that it holds no personal data is read from the context class only, not verified against a database. Backups: dumps in the object store still contain the erased data until retention deletes them (about 4 weeks). State that in the answer to the person; a dump is never edited. After a restore from backup, **re-run all erasures requested since that dump**. | `[not run]` |
| 8.8 | Keep a minimal record of each erasure (date, which of the four steps completed, no personal data) outside the repository. | `[not run: no process owner yet, section 11]` |

## 9. A suspended practitioner with future bookings

Suspension: an administrator calls `POST /api/v1/admin/practitioners/{id}/suspend` with `{ "reason": "<text>" }` on Directory (from `Approved`; `reinstate` goes back). Effect in the code: the profile is hidden; **Booking refuses new bookings and shows no slots** (it reads the status through the offering lookup: `422`, code `PractitionerNotBookable`). **Nothing cancels or moves the bookings that already exist**: Booking has no reference to the suspension beyond those two checks. A Directory erasure (section 8.2) also leaves the practitioner `Suspended`, so the same applies.

| # | Step | Tag |
|---|---|---|
| 9.1 | List the future bookings of the practitioner: bookingdb, `SELECT id, status, start_utc, client_user_id FROM bookings WHERE practitioner_id = '<practitionerId>' AND status IN ('PendingPayment','Confirmed') AND end_utc > now() ORDER BY start_utc;` (over `fly proxy`, section 6.4). `PendingPayment` rows expire by themselves after the 10 minute hold. | `[not run: no database]` |
| 9.2 | Ask the practitioner to cancel each `Confirmed` booking from their account: `POST /api/v1/bookings/{id}/cancel` allows the practitioner of the booking at any time before the end, **without a check of their profile status**, so a suspended practitioner can still do it. Effect: `CancelledByPractitioner`, full refund (ledger `Refund`), `practitioner_cancel_count` + 1. Tell the clients. | `[not run: no app]` Cancel rules read in `CancelFeature` and CONTRACTS §8.1. |
| 9.3 | If the practitioner cannot or will not cancel: **the system has no administrator cancel**. An administrator can only resolve a `Disputed` booking (`POST /api/v1/admin/bookings/{id}/resolve`). The supported path is: the client cancels 24 h or more ahead (full refund) or, if the practitioner does not appear, opens a dispute of kind `PractitionerNoShow` after the start, within the dispute window, and the administrator resolves with `refund_full`. Do **not** change booking, payment or ledger rows by hand. Section 11, item 7 (needs a code change). | `[not run: no app]` |
| 9.4 | Reinstating: `POST /api/v1/admin/practitioners/{id}/reinstate`; existing bookings are untouched. | `[not run: no app]` |

## 10. Reaching a private app

`fly proxy <local>:<remote> -a <app>` forwards a local port to the app's private port: `15432:5432` for PostgreSQL, `18080:8080` for a service. It needs `flyctl` logged in as the owner. `[not run: no Fly account]`

## 11. Open questions, gaps and decisions for the owner

1. **Encryption of the dumps** (personal data, brief and review texts): server-side encryption of the bucket, or `rclone crypt` with a key the owner holds? Also which provider and region for the bucket (an EU region, because of GDPR).
2. **Staging restore target:** a second small PostgreSQL machine for restore tests, or the production instance with scratch databases (as the scripts allow)?
3. **E-mail credentials:** the configuration names for the SMTP credentials do not exist in the repository's code; 5.6 cannot be written until they do.
4. **Administrator-initiated erasure in authservice** and **looking up a user id**: the routes are not in `docs/spikes/authservice.md`; they need a read of `AdminController` upstream.
5. **Who runs the monthly restore test (2.4) and the erasure record (8.8)?**
6. **Gap A (code):** the signature-failure burst is logged as a `Warning` without a stable event id; SPEC §8 asks for an `Error` with one. Only the amount-mismatch line has the `ALERT` prefix in the code.
7. **Gap B (code):** no administrator cancel and no cascade when a practitioner is suspended or erased (section 9.3).
8. **Gap C (code or docs):** `Internal__Key` has no overlap period during a rotation (5.3); a second accepted key would remove the gap.
