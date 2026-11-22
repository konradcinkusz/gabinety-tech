# DEPLOY: the tag-driven deploy to staging (T-09.03)

`.github/workflows/deploy.yml` (steps of one app: `_deploy-app.yml`) deploys to Fly.io when a `v*` tag is pushed. **Nothing here has run against Fly yet** (the accounts and secrets are T-09.01, owner work); the workflow is statically validated (actionlint) and its scripts are unit-tested (`scripts/tests/deploy.test.sh`). The acceptance of T-09.03 (a tag deploys a cold estate to staging, every app healthy) is pending T-09.01.

## 1. Cut a deploy

```
git switch main && git pull
git tag v0.1.0          # a new version every time; never move or reuse a tag
git push origin v0.1.0
```

The run: `verify-tag` (the commit must be on the default branch, else it fails and nothing is deployed) → `detect-changes` → `build` (matrix) → `deploy-postgres` → `deploy-authservice` → `deploy-directory` → `deploy-booking` → `deploy-reputation` → `deploy-assistant` → `deploy-web` (the order of SPEC 9) → `summary` (one table; the run fails if any job failed) → `mark-deployed` (only when the whole run succeeded). The run page shows each app's smoke line.

- **What is deployed.** Per app, only what changed since the **last fully successful deploy**, which is the marker tag `deployed/staging` (moved to the tagged commit by `mark-deployed` only after the whole run succeeded; the previous `v*` tag is the fallback only while no marker exists): the app's own paths plus, for the .NET services, the shared paths (`Directory.Packages.props`, `Directory.Build.props`, `global.json`, `ServiceDefaults`, `Contracts`). The table is `deploy/estate.conf` (add a project that a service references to its row). **An app that does not exist on Fly is always selected**, so the first tag against an empty organisation creates and deploys everything; no first tag also selects everything. An app that is not selected is shown as "not selected (unchanged)".
- **Images.** Built once per selected app and pushed to the registry `IMAGE_REGISTRY` (default `ghcr.io/<owner>/gabinety-<app>`, see section 4) with two immutable tags, `<release tag>` and `sha-<commit>`; deploys use the `sha-` tag. There is no `latest`. `postgres` and `authservice` are not built here: their image is pinned in their `fly.toml`.
- **Networks.** The `network` column of `deploy/estate.conf`: `public` apps (web, authservice, Booking for its webhook path) are deployed normally; `private` apps (Directory, Reputation, PostgreSQL) with `--no-public-ips`; `flycast` (the Assistant) also gets a private IPv6 once (`fly ips allocate-v6 --private`, idempotent) so `<app>.flycast` resolves and the proxy can start its stopped machine (COST.md).
- **Concurrency.** One group `deploy-staging`; a second tag waits for the first and is never cancelled mid-estate.
- **Smoke.** Public apps (web `/api/health`, authservice `/health/ready`) are asked over HTTPS; the private ones are probed inside their machine (`fly ssh console`, backends `/alive`, PostgreSQL `pg_isready`), each with 12 tries 10 s apart. The Assistant (`min_machines_running = 0`) passes with "skipped" when no machine is running. After authservice, `scripts/assert-jwks-one-key.sh` asserts exactly one JWKS key.

## 2. What the owner configures first (T-09.01), once

GitHub: repository → Settings → Environments → **`staging`**. Use no required reviewers (every job that names the environment would wait for approval) and restrict deployment to tags `v*` if you like. Names only; values go in the GitHub UI, never in a chat or a file.

| Kind | Name | Used for |
|---|---|---|
| secret | `FLY_API_TOKEN` | a Fly token that may create apps and deploy in the organisation (`fly tokens create org`) |
| secret | `POSTGRES_PASSWORD` | the database superuser; also inside every connection string (generate with `openssl rand -base64 33`; no `;` or `"`) |
| secret | `AUTH_JWT_PRIVATE_KEY_PEM` | authservice signing key, from `scripts/gen-authservice-key.sh` (only authservice holds it) |
| secret | `INTERNAL_KEY_BOOKING_TO_DIRECTORY`, `INTERNAL_KEY_REPUTATION_TO_DIRECTORY`, `INTERNAL_KEY_REPUTATION_TO_BOOKING`, `INTERNAL_KEY_DIRECTORY_TO_BOOKING` (ADR-0012: Directory asks Booking to cancel a suspended practitioner's bookings), `INTERNAL_KEY_REPUTATION_TO_ASSISTANT` | one internal key per caller and callee pair (ADR-0007, F-03): each at least 43 characters, all different, `openssl rand -base64 33`; the services refuse to start otherwise |
| secret, optional | `RATE_LIMIT_TRUSTED_PROXY_KEY` | `RateLimit__TrustedProxyKey` on web and the four backends (T-10.03): the same value everywhere, 43+ characters, different from every internal key; not an internal key, opens no route |
| secret, optional | `INTERNAL_KEY_OWNER_DIRECTORY`, `INTERNAL_KEY_OWNER_BOOKING`, `INTERNAL_KEY_OWNER_REPUTATION`, `INTERNAL_KEY_OWNER_ASSISTANT` | the owner's own caller key for the RUNBOOK 8 erasure hooks |
| secret, optional | `AUTH_GITHUB_CLIENT_ID`, `AUTH_GITHUB_CLIENT_SECRET` | GitHub sign-in (set both or neither) |
| secret, optional | `PAYMENTS_WEBHOOK_SECRET` | `Payments__WebhookSecret` of Booking (T-09.05 adds the provider API key) |
| variable, optional | `IMAGE_REGISTRY` | the image registry: `ghcr.io` (default) or `registry.fly.io`; see the owner decision in section 4 |
| variable, optional | `FLY_ORG` | the Fly organisation slug (default `personal`) |
| variable, optional | `PUBLIC_URL_AUTHSERVICE`, `PUBLIC_URL_WEB` | public origins for the smoke and the JWKS assertion (default `https://<fly-app>.fly.dev`) |

The mapping from these names to Fly secret names is `deploy/secrets.map` (SPEC 11: each service sees only its own). A missing *required* secret fails that app's deploy naming the GitHub secret (never a value); a missing *optional* one is skipped and the feature degrades (P8). The provider, e-mail and model keys of T-09.05 to T-09.07 are added to the map by those tasks.

Also before the first tag: the `<...-host>` placeholders of `deploy/fly/authservice.fly.toml` are filled (the workflow refuses to deploy a config that still has one), and the registry decision of section 4 is taken.

Network (F-03): `deploy/estate.conf` marks each app public, private or flycast. A private or flycast app is deployed with `--no-public-ips` and the job then runs `scripts/deploy/assert-no-public-ip.sh`, which fails when the app has any public address. Booking is public but its code serves the public edge only the webhook, `/alive` and `/api/v1/*` (not admin); `/internal/*` is 404 from the edge.

**Migration of the internal keys (F-03), once, before the first deploy that contains this change:** the old secrets `INTERNAL_KEY_DIRECTORY`, `INTERNAL_KEY_BOOKING`, `INTERNAL_KEY_ASSISTANT`, `INTERNAL_KEY_REPUTATION` (one key per callee, shared by its callers) are replaced by one secret per caller and callee pair. (1) Create `INTERNAL_KEY_BOOKING_TO_DIRECTORY`, `INTERNAL_KEY_REPUTATION_TO_DIRECTORY`, `INTERNAL_KEY_REPUTATION_TO_BOOKING`, `INTERNAL_KEY_REPUTATION_TO_ASSISTANT` in the GitHub environment `staging`, each a fresh `openssl rand -base64 33`, all different, and different from `RATE_LIMIT_TRUSTED_PROXY_KEY`. (2) Deploy (a tag that touches the four backends, or re-run the failed jobs): the callee and its callers must be deployed in the same run, or between the two deploys internal calls answer 401 (the same gap as a key rotation; RUNBOOK 5.3). (3) The apps keep the old `Internal__Key` as a Fly secret until it is removed: `fly secrets unset --stage -a gabinety-<app>-staging Internal__Key` for directory, booking, reputation and assistant, then deploy; otherwise the old shared key stays valid and the startup check rejects it if it is weak. (4) Delete the four old GitHub secrets. A key that was ever logged or shared is rotated, not reused.

Secrets are set with `fly secrets set --stage` and take effect with the deploy that follows in the same job. Values are registered with `::add-mask::` before use, the scripts never run with `set -x`, and a value is never written to the log or the summary. **A secret changed in GitHub is not a change in git**: it does not select an app; re-run the failed run's jobs, or push a new tag that touches the app.

## 3. Rollback

- **One app.** Find the previous good image (`fly releases --app gabinety-<app>-staging --image`) and redeploy it: `fly deploy --app gabinety-<app>-staging --config deploy/fly/<app>.fly.toml --image ghcr.io/<owner>/gabinety-<app>:sha-<commit> --ha=false`. For `authservice` and `postgres` the image is the one pinned in their `fly.toml`: revert that change in git instead.
- **Everything.** Revert on `main`, tag the revert as a new version (`v0.1.1`); never delete or move a tag that has been deployed. The next run deploys what changed between the two tags.
- **Database schema.** Services migrate on start after the listener is up; a rollback of an image does not roll back the schema. Migrations are written to be backward compatible for one release (a rollback must start on the newer schema).
- Failed run halfway: fix forward with a new tag, or re-run the failed jobs. Either is safe: the marker `deployed/staging` did not move, so the next run still selects every app whose change was in the failed run. To make an app redeploy regardless, delete the marker (`git push origin :refs/tags/deployed/staging`): the next run falls back to the previous release tag (and always selects an app that does not exist).

## 4. Owner decisions and open points

- **BLOCKED: Booking cannot be deployed until T-09.05 (security review F-01, T-10.08).** Mock payments are the fake profile's. The Booking host refuses to start outside it (`GABINETY_FAKE=1` or `Environment__Kind=Development`) with `Payments__Provider=Mock`, with no value, or with a vendor name that has no adapter registered, and `POST /api/v1/payments/simulate` is not mapped there. `deploy/fly/booking.fly.toml` therefore carries the placeholder `Payments__Provider = "<payments-provider-T-09-05>"`, and `scripts/deploy/check-placeholders.sh` (the step "The config has no placeholder left" of `_deploy-app.yml`) fails the Booking deploy while it stands. This is expected: the run stops at `deploy-booking` and every app after it (`reputation`, `assistant`, `web`) is skipped when Booking changed in the tagged range. T-09.05 builds the provider adapter and replaces the placeholder with the adapter's name; `Payments__WebhookSecret` must be set too, because with no secret every webhook of a non-Mock provider is rejected. Never work around it by setting `Payments__Provider=Mock`, `GABINETY_FAKE` or `Environment__Kind`: `scripts/check-fly-toml.sh` refuses a `fly.toml` that names Mock.
- **OWNER DECISION: the image registry.** `IMAGE_REGISTRY` is the single switch (a repository or `staging` environment variable; default `ghcr.io`, as SPEC 9 says).
  - `ghcr.io`: Fly pulls `ghcr.io/...` **anonymously**, so the `gabinety-*` packages must be public. That is fine only if the repository and the images contain nothing private. The workflow does not make a package public; the owner does (GitHub, Packages, package settings), after the first push creates it.
  - `registry.fly.io`: private to the Fly organisation, no public images; the build job creates each app first (the registry repository is the app) and logs in with `FLY_API_TOKEN`. Recommended if anything in the images should stay private.
  Any other registry value fails the build job.
- **Marker tag.** `deployed/staging` is force-moved by `mark-deployed` (the only job with `contents: write`). A tag ruleset that protects tags must allow that workflow to update `deployed/*`; the marker does not match the `v*` trigger.
- **Private apps' smoke** uses `fly ssh console` and a bash `/dev/tcp` HTTP GET (the backend images have bash, no curl or wget: the Dockerfile HEALTHCHECK does the same); PostgreSQL uses `pg_isready`.
- **The called workflow's secrets.** `_deploy-app.yml` reads the environment's secrets through `secrets: inherit` and `toJSON(secrets)`. That is the documented behaviour but it was not run.
- **Not validated here:** `flyctl` flags (`apps create --org`, `volumes create --yes`, `secrets set --stage`, `deploy --ha=false --no-public-ips --yes`, `ips allocate-v6 --private`, `ssh console --command`) against v0.5.12, the staged-secret behaviour on an app with no machine, and the `fly.toml` files against `fly config validate`.
