# CUTOVER: from Next.js (`web/`) to the Blazor Web App (`src/Gabinety.Web`)

**Status: sections 0 and 1 are done (the cutover PR, 2026-11-22); sections 2 to 5 are the owner's go-live runbook and are not done.** ADR-0010 (proposal) put the Blazor Web App in place of the Next.js
BFF. The PR removed `web/` and everything that depended on it; **no deploy, DNS or Fly step was run** (they need the owner's accounts). The old Fly app `gabinety-web-staging` is untouched and still serves
the public host: it is the rollback until section 3 step 6.

## 0. Preconditions (as they stood when the PR was made)

| # | Precondition | Result |
|---|---|---|
| 1 | **Parity**: every Next route has a Blazor route or a documented exemption | `CUTOVER_GATE=1 node scripts/check-parity.mjs`: 44 Next routes, 35 ported, 9 exempt (the `/api/*` BFF), 0 missing. The gate is deleted with `web/` (`docs/operations/PARITY.md` is the archive) |
| 2 | **Journeys J1-J7 pass on the Blazor host with no skips** | `HOST=blazor PARITY_STRICT=1 pnpm --dir tests/e2e test`: 18 passed, none skipped (run on this branch before `web/` was removed, and by the `e2e` job on the PR) |
| 3 | The Next journeys still pass (the last proof before `web/` went) | the `e2e` job of `main` before the PR |
| 4 | `dotnet build -warnaserror && dotnet test --filter "tier!=integration"` green, the Blazor image builds | CI: `build-test`, `docker-build-backend (web)` |
| 5 | **Deploys do not sign users out**: the stateless encrypted session cookie landed | landed (the session cookie is sealed with Data Protection, `AppHost` and `web-blazor.fly.toml` keep the key ring on a persisted path/volume) |
| 6 | **Output cache** of the anonymous public pages (five minutes) landed | landed (`PERFORMANCE.md`; the AppHost turns it off for the journeys) |
| 7 | **Memory measured** and the 512 MB machine confirmed | `PERFORMANCE.md` section 8, `COST.md` section 4: 269 MB RSS with 150 idle interactive pages, 512 MB kept. Not re-measured on the final build (the host code did not change in the cutover PR); re-run `ONLY_CIRCUITS=1 scripts/measure-performance.sh` before the go-live if the host changed since |
| 8 | Staging check on the Blazor app's own `*.fly.dev` host before the public host moves | **the owner's, section 3** |

## 1. The cutover PR (what it did)

1. **`web/` removed** (`git rm -r web`), with its Dockerfile, lockfile and lint config. `tests/e2e` stays (its own lockfile).
2. **AppHost**: the JavaScript resource and its `pnpm` step are gone; `Aspire.Hosting.JavaScript` left `Directory.Packages.props` and the AppHost project; the Blazor resource keeps the name `web-blazor`
   (the Fly key and the estate row too) and **moved from port 3001 to 3000** with `Site__BaseUrl = http://localhost:3000`; it now also waits for authservice (the Next resource did).
3. **E2E harness**: one host. `harness/host.ts`, `harness/parity.ts`, the `HOST` switch, `PARITY_STRICT`, every `isBlazor` branch and `requirePorted` are gone; the default URL is `http://localhost:3000`, the health path
   `/health`; the Next-only J7 variant is deleted and the Blazor J7 became `j7-foreign-origin.spec.ts`. `ci.yml`: the `e2e` matrix is one job named `e2e` (required check), the `Route parity` step and the `web` job are gone,
   the `pnpm --dir web install` lines and the Vitest step of the `integration` job too (see "Out-of-scope" in the PR: the Vitest test was the only real-authservice test of the web's handlers),
   `docker-build-web` is gone and the `web-blazor` row of `docker-build-backend` is now `web` (required check `docker-build-backend (web)`).
4. **Scripts**: `check-fake-profile.sh` (one web line, `:3000/health`), `perf-stack.sh` and `measure-performance.sh` (the Blazor host, a Release publish; the Lighthouse and load rows stay),
   `check-private-topology.sh` (only `web-blazor.fly.toml`; the public apps are `authservice booking web-blazor`).
5. **Estate (the switch)**: in `deploy/estate.conf` the `web` row is gone and the `web-blazor` row is live; `deploy.yml` has no `deploy-web` job (its `needs`/`outputs` entries are removed; `deploy-web-blazor` runs);
   `deploy/secrets.map` has the `web-blazor` row only; `_deploy-app.yml` reads the optional variable `PUBLIC_URL_WEB_BLAZOR` (it was `PUBLIC_URL_WEB`, which names the old host: do not reuse it);
   `scripts/tests/deploy.test.sh` asserts the row is live. **Deviation from the plan above this section had:** `deploy/fly/web.fly.toml` is deleted *now*, not after the old app is gone, because it named
   `web/Dockerfile` and `scripts/check-fly-toml.sh` rejects a config whose Dockerfile does not exist. Nothing needs it to keep the old app running (`fly scale`, `fly apps destroy` take no config);
   to redeploy the OLD app, check out the commit before the cutover (section 4).
6. **Docs and badges**: `README.md` (Blazor badge, quick start without `pnpm --dir web install`, port table), `docs/GETTING-STARTED.md` (one host; the "J3 by curl" recipe now goes to the services with a
   DevIdentity token because the BFF `/api/auth` and `/api/proxy` are gone), `tests/e2e/README.md`, `docs/operations/{DEPLOY,RUNBOOK,COST,PERFORMANCE,ROUTE-AUTH,AUTH,CI}.md`, `PARITY.md` ("done on 2026-11-22",
   keeps the route map and the one exemption); `scripts/check-parity.mjs`, its test and `scripts/parity-exemptions.json` are deleted. `docs/adr-proposals/0010` is **not** moved: that is the owner's step.
7. **Secrets**: nothing to do. The Blazor host needs only `RATE_LIMIT_TRUSTED_PROXY_KEY` (optional, mapped for `web-blazor`) in the GitHub `staging` environment; `FLY_API_TOKEN` and the rest are unchanged.
   The `web` row of `secrets.map` is gone; the secret already staged on the old Fly app stays there until it is destroyed.
8. **`CUTOVER_GATE`**: the repository variable no longer means anything (nothing reads it). **The owner deletes it** in the repository settings (an agent has no access to variables).

Left untouched on purpose: the GitHub Pages landing page (`site/`, `.github/workflows/pages.yml`).

## 2. Deploy (first tag after the PR merges)

The first tag after the PR merges deploys `web-blazor` (it is the only web row now) as its own Fly app, **`gabinety-web-blazor-staging`** (decision of ADR-0012, supervisor decisions: a new app beside the old one, so the old one is the rollback and no deploy ever mutates the live site in place): the workflow creates the app and the 1 GB volume `webdata` for the Data Protection keys, stages the secret, deploys one machine (`--ha=false`), fixes the volume owner (`chown -R app /data`) and smokes `/alive` over HTTPS.

## 3. Moving the public host, then retiring the old app (a person, `flyctl`)

1. Open `https://gabinety-web-blazor-staging.fly.dev/` and walk the demo: home, a category, a profile, sign in, a booking to the mock-free checkout (staging uses the real Stripe test mode), the admin queue.
2. Check the websocket: DevTools, Network, WS: one `_blazor` connection stays open and answers. Check `fly logs --app gabinety-web-blazor-staging` for `Origin` refusals.
3. Move the certificate and domain: `fly certs add <host> --app gabinety-web-blazor-staging`, then remove it from the old app (`fly certs remove <host> --app gabinety-web-staging`) and point DNS as `fly certs show` says. `Site__BaseUrl` in `web-blazor.fly.toml` must already be that host (the origin guard allows exactly it); the OAuth redirect allow-list of authservice (`OAuth__PostLoginRedirectAllowedBaseUrls`) and the GitHub OAuth app callback keep the host, so they do not change when only the host moves. If the host **changes**, update both and `Identity__PublicUrl`.
4. Everybody is signed out once (the Next cookies are not the Blazor ones). **Announce it**: a banner or a line in the release notes the day before: "after the update you sign in again" (later deploys do not sign anybody out: the session cookie is sealed with the persisted key ring).
5. Watch for 24 hours: `fly status`, memory (`fly machine status`), the 429 rate on the services (the visitor address must arrive: a spike of 429 for everybody means `RateLimit__TrustedProxyKey` differs between the host and the backends), 5xx.
6. After a week without a rollback: `fly scale count 0 --app gabinety-web-staging`, a month later `fly apps destroy gabinety-web-staging`. (Its config file and secrets row were already removed by the cutover PR, step 1.5; the repository variable `PUBLIC_URL_WEB`, if the owner set it, can go too.)

## 4. Rollback

Before step 3.3: nothing to roll back (the old host never moved). After it: re-point the host's certificate and DNS to `gabinety-web-staging` (still running, scaled to 1). To make `main` match, revert the cutover PR and tag the revert; a revert brings back `web/`, `deploy/fly/web.fly.toml` and the old estate row, so the next deploy would redeploy the Next.js app into `gabinety-web-staging`. Sessions are lost both ways. `DEPLOY.md` section 3 covers an image rollback of the Blazor app.

## 5. Announce

Release note: the new frontend, the one-time sign-in, what is unchanged (URLs, accounts, bookings). The owner moves ADR-0010 to `gabinety-tech-spec/docs/adr/` as accepted and applies the SPEC/CONTRACTS edits listed at the end of the ADR.
