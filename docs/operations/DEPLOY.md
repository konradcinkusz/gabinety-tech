# DEPLOY: the tag-driven deploy to staging (T-09.03)

`.github/workflows/deploy.yml` (steps of one app: `_deploy-app.yml`) deploys to Fly.io when a `v*` tag is pushed. **Nothing here has run against Fly yet** (the accounts and secrets are T-09.01, owner work); the workflow is statically validated (actionlint) and its scripts are unit-tested (`scripts/tests/deploy.test.sh`). The acceptance of T-09.03 (a tag deploys a cold estate to staging, every app healthy) is pending T-09.01.

## 1. Cut a deploy

```
git switch main && git pull
git tag v0.1.0          # a new version every time; never move or reuse a tag
git push origin v0.1.0
```

The run: `verify-tag` (the commit must be on the default branch, else it fails and nothing is deployed) → `detect-changes` → `build` (matrix) → `deploy-postgres` → `deploy-authservice` → `deploy-directory` → `deploy-booking` → `deploy-reputation` → `deploy-assistant` → `deploy-web` (the order of SPEC 9) → `summary` (one table; the run fails if any job failed). The run page shows each app's smoke line.

- **What is deployed.** Per app, only what changed since the previous `v*` tag: the app's own paths plus, for the .NET services, the shared paths (`Directory.Packages.props`, `Directory.Build.props`, `global.json`, `ServiceDefaults`, `Contracts`). The table is `deploy/estate.conf` (add a project that a service references to its row). **An app that does not exist on Fly is always selected**, so the first tag against an empty organisation creates and deploys everything; no first tag also selects everything. An app that is not selected is shown as "not selected (unchanged)".
- **Images.** Built once per selected app and pushed to `ghcr.io/<owner>/gabinety-<app>` with two immutable tags, `<release tag>` and `sha-<commit>`; deploys use the `sha-` tag. There is no `latest`. `postgres` and `authservice` are not built here: their image is pinned in their `fly.toml`.
- **Concurrency.** One group `deploy-staging`; a second tag waits for the first and is never cancelled mid-estate.
- **Smoke.** Public apps (web `/api/health`, authservice `/health/ready`) are asked over HTTPS; the private ones are probed inside their machine (`fly ssh console`, backends `/alive`, PostgreSQL `pg_isready`), each with 12 tries 10 s apart. The Assistant (`min_machines_running = 0`) passes with "skipped" when no machine is running. After authservice, `scripts/assert-jwks-one-key.sh` asserts exactly one JWKS key.

## 2. What the owner configures first (T-09.01), once

GitHub: repository → Settings → Environments → **`staging`**. Use no required reviewers (every job that names the environment would wait for approval) and restrict deployment to tags `v*` if you like. Names only; values go in the GitHub UI, never in a chat or a file.

| Kind | Name | Used for |
|---|---|---|
| secret | `FLY_API_TOKEN` | a Fly token that may create apps and deploy in the organisation (`fly tokens create org`) |
| secret | `POSTGRES_PASSWORD` | the database superuser; also inside every connection string (generate with `openssl rand -base64 33`; no `;` or `"`) |
| secret | `AUTH_JWT_PRIVATE_KEY_PEM` | authservice signing key, from `scripts/gen-authservice-key.sh` (only authservice holds it) |
| secret | `INTERNAL_KEY_DIRECTORY`, `INTERNAL_KEY_BOOKING`, `INTERNAL_KEY_ASSISTANT`, `INTERNAL_KEY_REPUTATION` | the callee keys of ADR-0007; each caller receives the keys of the callees it calls |
| secret, optional | `AUTH_GITHUB_CLIENT_ID`, `AUTH_GITHUB_CLIENT_SECRET` | GitHub sign-in (set both or neither) |
| secret, optional | `PAYMENTS_WEBHOOK_SECRET` | `Payments__WebhookSecret` of Booking (T-09.05 adds the provider API key) |
| variable, optional | `FLY_ORG` | the Fly organisation slug (default `personal`) |
| variable, optional | `PUBLIC_URL_AUTHSERVICE`, `PUBLIC_URL_WEB` | public origins for the smoke and the JWKS assertion (default `https://<fly-app>.fly.dev`) |

The mapping from these names to Fly secret names is `deploy/secrets.map` (SPEC 11: each service sees only its own). A missing *required* secret fails that app's deploy naming the GitHub secret (never a value); a missing *optional* one is skipped and the feature degrades (P8). The provider, e-mail and model keys of T-09.05 to T-09.07 are added to the map by those tasks.

Also before the first tag: the `<...-host>` placeholders of `deploy/fly/authservice.fly.toml` are filled (the workflow refuses to deploy a config that still has one), and the GHCR packages are readable by Fly (see "Open points").

Secrets are set with `fly secrets set --stage` and take effect with the deploy that follows in the same job. Values are registered with `::add-mask::` before use, the scripts never run with `set -x`, and a value is never written to the log or the summary. **A secret changed in GitHub is not a change in git**: it does not select an app; re-run the failed run's jobs, or push a new tag that touches the app.

## 3. Rollback

- **One app.** Find the previous good image (`fly releases --app gabinety-<app>-staging --image`) and redeploy it: `fly deploy --app gabinety-<app>-staging --config deploy/fly/<app>.fly.toml --image ghcr.io/<owner>/gabinety-<app>:sha-<commit> --ha=false`. For `authservice` and `postgres` the image is the one pinned in their `fly.toml`: revert that change in git instead.
- **Everything.** Revert on `main`, tag the revert as a new version (`v0.1.1`); never delete or move a tag that has been deployed. The next run deploys what changed between the two tags.
- **Database schema.** Services migrate on start after the listener is up; a rollback of an image does not roll back the schema. Migrations are written to be backward compatible for one release (a rollback must start on the newer schema).
- Failed run halfway: re-run the failed jobs of the same run (same tag, same selection). Do not push a new tag to "fix" it unless the fix is in the commit: the next tag diffs against the failed tag, so apps whose change was in the failed tag but never deployed are NOT selected again (see below).

## 4. Open points

- **Previous tag, not previous *successful* deploy.** Detection diffs against the previous tag as the standards say. If a tag's run failed before an app was deployed and a later tag does not touch that app, the app stays on its old image. Mitigation today: re-run the failed jobs of the failed run. A durable fix (compare against the image each Fly app really runs) is not done.
- **Private GHCR packages.** Fly pulls `--image ghcr.io/...` anonymously, so the packages must be public (GitHub → Packages → settings) or Fly needs registry credentials. Which one is a decision for the owner/supervisor.
- **The called workflow's secrets.** `_deploy-app.yml` reads the environment's secrets through `secrets: inherit` and `toJSON(secrets)`. That is the documented behaviour but it was not run.
- **Not validated here:** `flyctl` flags (`apps create --org`, `volumes create --yes`, `secrets set --stage`, `deploy --ha=false --yes`, `ssh console --command`) against v0.5.12, the staged-secret behaviour on an app with no machine, and `fly.toml` syntax.
