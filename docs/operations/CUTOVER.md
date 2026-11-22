# CUTOVER: from Next.js (`web/`) to the Blazor Web App (`src/Gabinety.Web`)

**Status: prepared, not executed.** ADR-0010 (proposal) puts the Blazor Web App in place of the Next.js BFF. Two hosts run side by side until this list is done; this document makes the
final PR mechanical. **Do not merge it as part of any port PR**: it is the checklist of the cutover PR, which one person does in one sitting. Nothing here is done by an earlier PR except
what is marked *ready*.

## 0. Preconditions (all must be true; the gate checks the first four)

| # | Precondition | How it is checked |
|---|---|---|
| 1 | **Parity**: every Next route has a Blazor route or a documented exemption | `CUTOVER_GATE=1 node scripts/check-parity.mjs` exits 0 (CI: repository variable `CUTOVER_GATE=1`); `docs/operations/PARITY.md` has no `not ported` row |
| 2 | **Journeys J1-J7 pass on the Blazor host with no skips** | CI job `e2e-blazor` green; locally `HOST=blazor PARITY_STRICT=1 pnpm --dir tests/e2e test` (a skip is a failure) |
| 3 | The Next journeys still pass (the last proof before `web/` goes) | CI job `e2e` green |
| 4 | `dotnet build -warnaserror && dotnet test --filter "tier!=integration"` green, `docker-build-backend (web-blazor)` green | CI |
| 5 | **Deploys do not sign users out**: the stateless encrypted session cookie landed (ADR-0010 cutover preconditions) | its PR; if it did not land the cutover signs everybody out at every deploy: decide knowingly (`docs/operations/RUNBOOK.md` "Blazor web") |
| 6 | **Output cache** of the anonymous public pages (five minutes) landed | its PR (`PERFORMANCE.md`) |
| 7 | **Memory measured** and the 512 MB machine confirmed or resized | `PERFORMANCE.md` "Blazor circuits", `COST.md`; re-run `ONLY_CIRCUITS=1 scripts/measure-performance.sh` on the final build |
| 8 | Staging check on the Blazor app's own `*.fly.dev` host before the public host moves (section 3) | a person |

## 1. The cutover PR (one commit, in this order)

1. **Remove `web/`** (`git rm -r web`), including `web/Dockerfile`, `web/pnpm-lock.yaml` and its lint config. Keep `tests/e2e` (it has its own lockfile).
2. **AppHost**: in `src/Gabinety.AppHost/Program.cs` delete the JavaScript resource (`AddJavaScriptApp` for `web/`, the `pnpm` install step and its `WaitFor`s); the Blazor resource keeps the name `web-blazor` for now (rename to `web` only if the e2e `wait-for-profile` and `check-fake-profile.sh` are renamed in the same commit) and **moves from port 3001 to 3000** (`WithHttpEndpoint(port: 3000, ...)`, `Site__BaseUrl = http://localhost:3000`); drop `Aspire.Hosting.JavaScript` from `Directory.Packages.props` and the AppHost project; `OAuth__PostLoginRedirectBaseUrl` / `...AllowedBaseUrls__0` in CI already say `:3000` and stay.
3. **E2E harness**: in `tests/e2e/harness/env.ts` make `blazor` the only host: delete the `HOST` switch, the default web URL becomes `http://localhost:3000`, the health path `/health`; delete the `isBlazor` branches (`harness/auth.ts`, `booking-support.ts`, `booking-api.ts`), `requirePorted` and `harness/parity.ts` (and its calls in `journeys/`); in `.github/workflows/ci.yml` collapse the `e2e` matrix to one job named `e2e` (keep the check name: it is a required check) and drop the `Route parity` step; remove the `web` job and the `pnpm --dir web install` lines (e2e, integration); remove the `docker-build-web` job and rename the `web-blazor` entry of `docker-build-backend` to `web`.
4. **Scripts**: `scripts/check-fake-profile.sh` (the `web` and `webblazor` lines become one on :3000 `/health`), `scripts/perf-stack.sh` and `scripts/measure-performance.sh` (the Next production build and `WEB_URL=http://localhost:3000/api/health` give way to the Blazor host; the Lighthouse and load rows stay), `scripts/check-private-topology.sh` (drop `deploy/fly/web.fly.toml` from the loop), `scripts/check-fly-toml.sh` (nothing: it checks both files by name).
5. **Estate (the switch)**: in `deploy/estate.conf` comment out the `web` row and **uncomment the `#cutover# web-blazor` row** (delete the `#cutover# ` prefix); in `.github/workflows/deploy.yml` delete the `deploy-web` job and its entries in the `needs` of `summary` and the `detect-changes` outputs (the `deploy-web-blazor` job is already there and starts running when the row exists); delete `deploy/fly/web.fly.toml` and the `web` rows of `deploy/secrets.map` once the old Fly app is gone (section 3 step 6). `scripts/tests/deploy.test.sh` already checks the row as if enabled; after the switch its "commented out in the live estate" check flips: change it to assert the row is live.
6. **Docs and badges**: `README.md` (the Next.js badge becomes a .NET/Blazor one, the quick start loses `pnpm --dir web install`, the port table loses `web/` and `src/Gabinety.Web` moves to 3000), `docs/GETTING-STARTED.md` (the two-host rows, `HOST=` text), `tests/e2e/README.md`, `docs/operations/PARITY.md` (archive: replace by "done on <date>", keep the exemptions file), delete `scripts/check-parity.mjs` and `scripts/parity-exemptions.json` and the `CUTOVER_GATE` variable. `docs/operations/{DEPLOY,RUNBOOK,COST,PERFORMANCE,ROUTE-AUTH}.md`: the Next sections go, the Blazor ones become the only ones. `docs/adr-proposals/0010` is moved to the specification repository as accepted by the owner (their step, not this PR's).
7. **Secrets**: the Blazor host needs only `RATE_LIMIT_TRUSTED_PROXY_KEY` (optional, already mapped for `web-blazor`) in the GitHub `staging` environment; it needs no new secret. `FLY_API_TOKEN` and the rest are unchanged. Nothing to rotate: the Next app held no signing secret.
8. **`CUTOVER_GATE`**: delete the repository variable (it is only meaningful while both hosts exist).

Verify the PR: `dotnet build -warnaserror && dotnet test --filter "tier!=integration"`, `scripts/tests/deploy.test.sh`, `scripts/check-fly-toml.sh`, `scripts/check-private-topology.sh`, and the CI jobs `e2e`, `docker-build-backend (web)`, `deploy-checks`, `secret-scan`.

## 2. Deploy (first tag after the PR merges)

The tag deploys `web-blazor` as its own Fly app, **`gabinety-web-blazor-staging`** (decision of ADR-0012, supervisor decisions: a new app beside the old one, so the old one is the rollback and no deploy ever mutates the live site in place): the workflow creates the app and the 1 GB volume `webdata` for the Data Protection keys, stages the secret, deploys one machine (`--ha=false`), fixes the volume owner (`chown -R app /data`) and smokes `/alive` over HTTPS.

## 3. Moving the public host, then retiring the old app (a person, `flyctl`)

1. Open `https://gabinety-web-blazor-staging.fly.dev/` and walk the demo: home, a category, a profile, sign in, a booking to the mock-free checkout (staging uses the real Stripe test mode), the admin queue.
2. Check the websocket: DevTools, Network, WS: one `_blazor` connection stays open and answers. Check `fly logs --app gabinety-web-blazor-staging` for `Origin` refusals.
3. Move the certificate and domain: `fly certs add <host> --app gabinety-web-blazor-staging`, then remove it from the old app (`fly certs remove <host> --app gabinety-web-staging`) and point DNS as `fly certs show` says. `Site__BaseUrl` in `web-blazor.fly.toml` must already be that host (the origin guard allows exactly it); the OAuth redirect allow-list of authservice (`OAuth__PostLoginRedirectAllowedBaseUrls`) and the GitHub OAuth app callback keep the host, so they do not change when only the host moves. If the host **changes**, update both and `Identity__PublicUrl`.
4. Everybody is signed out once (the Next cookies are not the Blazor ones). **Announce it**: a banner or a line in the release notes the day before: "after the update you sign in again" (and, until the stateless session cookie exists, after each deploy).
5. Watch for 24 hours: `fly status`, memory (`fly machine status`), the 429 rate on the services (the visitor address must arrive: a spike of 429 for everybody means `RateLimit__TrustedProxyKey` differs between the host and the backends), 5xx.
6. After a week without a rollback: `fly scale count 0 --app gabinety-web-staging`, a month later `fly apps destroy gabinety-web-staging`, delete `deploy/fly/web.fly.toml` and the `web` secrets rows (the last part of step 1.5).

## 4. Rollback

Before step 3.3: nothing to roll back (the old host never moved). After it: re-point the host's certificate and DNS to `gabinety-web-staging` (still running, scaled to 1) and revert the cutover PR on `main`; tag the revert. Sessions are lost both ways. `DEPLOY.md` section 3 covers an image rollback of the Blazor app.

## 5. Announce

Release note: the new frontend, the one-time sign-in, what is unchanged (URLs, accounts, bookings). The owner moves ADR-0010 to `gabinety-tech-spec/docs/adr/` as accepted and applies the SPEC/CONTRACTS edits listed at the end of the ADR.
