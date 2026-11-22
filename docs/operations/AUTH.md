# AUTH: the authservice and PostgreSQL apps on Fly (T-09.04)

Config lives in `deploy/fly/`. Nothing here is secret; `<placeholders>` are filled by the owner. Nothing in this note was run against Fly (no account yet, T-09.01); see section 8.

## 1. Apps

| App | Config | Image | Public | Health (Fly check) |
|---|---|---|---|---|
| `gabinety-authservice-staging` | `deploy/fly/authservice.fly.toml` | `ghcr.io/konradcinkusz/authservice@sha256:d0309a94...` (v0.3.4, digest in the spike) | yes | HTTP `GET /health/ready` on 8080 |
| `gabinety-postgres-staging` | `deploy/fly/postgres.fly.toml` + `init-databases.sql` | `postgres:17-alpine` | no | TCP 5432 |

Order of first deploy: PostgreSQL, then authservice, then the services (T-09.03). Services reach the database at `gabinety-postgres-staging.internal:5432`.

## 2. PostgreSQL

1. `fly apps create gabinety-postgres-staging --org <org>`
2. `fly volumes create pgdata --app gabinety-postgres-staging --region fra --size 5`
3. `fly secrets set --app gabinety-postgres-staging POSTGRES_PASSWORD="$(openssl rand -base64 33)"` (keep the value in the owner's password manager; it also goes into each connection string below)
4. `fly deploy --config deploy/fly/postgres.fly.toml`

`init-databases.sql` creates `authdb`, `directorydb`, `bookingdb`, `reputationdb`, `assistantdb`. The image runs it only when the data directory is empty (first start). It is idempotent, so on an existing volume run it by hand:
`fly proxy 15432:5432 --app gabinety-postgres-staging` then `psql -h localhost -p 15432 -U postgres -f deploy/fly/init-databases.sql`. Schemas are created by the services themselves (migrations on start / authservice `EnsureCreated`). One superuser serves all five databases on staging; per-service roles are a later hardening, not done here.

Connection string shape (a secret on each app, never in a file): `Host=gabinety-postgres-staging.internal;Port=5432;Database=<db>;Username=postgres;Password=<POSTGRES_PASSWORD>`.

## 3. authservice secrets

```
scripts/gen-authservice-key.sh ~/gabinety-staging-jwt.pem --fly gabinety-authservice-staging   # section 4
fly secrets set --app gabinety-authservice-staging --stage \
  "ConnectionStrings__DefaultConnection=Host=gabinety-postgres-staging.internal;Port=5432;Database=authdb;Username=postgres;Password=<POSTGRES_PASSWORD>"
fly deploy --config deploy/fly/authservice.fly.toml
```

Other apps need `Identity__Authority=https://<authservice-public-host>` and `Identity__Audience=gabinety` (the value of `Jwt__Audience`). Only authservice ever holds the signing key (P5).

## 4. Signing key

`scripts/gen-authservice-key.sh <out-file> [--fly <app>]` writes an RSA 2048 PKCS#8 PEM, mode 0600, refuses to overwrite, refuses a path inside a git work tree, and prints only the public key's SHA-256. `*.pem` is git-ignored as a second guard. Only the private key is configured (`Jwt__PrivateKeyPem`); authservice derives and publishes the public key at `/.well-known/jwks.json`. Rotation: set the new key as `Jwt__PrivateKeyPem` and the old one as `Jwt__PreviousPublicKeyPem` (spike section 5); the JWKS then has two keys for the overlap, so the assertion below needs `EXPECTED_KEYS=2` during a rotation (not implemented; edit the script or run it by hand).

## 5. `Jwt__PublicBaseUrl` and the OAuth callback

- `Jwt__PublicBaseUrl` = the public origin of authservice (`https://<authservice-public-host>`, `<app>.fly.dev` until a domain exists). It becomes `jwks_uri` in `/.well-known/openid-configuration`; without it authservice uses the request origin, which behind Fly's proxy is usually right but not guaranteed.
- GitHub sign-in is **optional** and off unless both `OAuth__GitHub__ClientId` and `OAuth__GitHub__ClientSecret` are set. Note DECISIONS 2026-11-16 (GitHub verification by bio code, no OAuth app); the OAuth app of T-09.01 item 5 is only for sign-in.
- If enabled: register the callback `https://<authservice-public-host>/signin-github` at GitHub (**not** `/api/external-auth/callback`, which the spike named: observed on v0.3.4, the redirect to GitHub carries `redirect_uri=<origin>/signin-github`, the GitHub handler's own callback path, and authservice then moves on to `/api/external-auth/callback` itself; `RealAuthFlowTests.GitHub_sign_in_start_...` asserts it). The `redirect_uri` origin is the one authservice sees on the request (behind Fly's proxy that depends on its forwarded-headers settings), so check the first redirect before the first real sign-in. Set `OAuth__CallbackBaseUrl` to `https://<authservice-public-host>`, and `OAuth__PostLoginRedirectBaseUrl` plus `__PostLoginRedirectAllowedBaseUrls__0` to the web origin. Then `fly secrets set --app gabinety-authservice-staging OAuth__GitHub__ClientId=<id> OAuth__GitHub__ClientSecret=<secret>`.

## 6. Deploy assertion: exactly one key

`scripts/assert-jwks-one-key.sh https://<authservice-public-host>` exits 0 only if `/.well-known/jwks.json` has exactly one key. An empty set (HS256, or a missing `Jwt__PrivateKeyPem`) fails. T-09.03 calls it after the authservice deploy. Local demonstration against stub servers (not against authservice): an empty `{"keys":[]}` gave `FAIL: JWKS has 0 keys, expected exactly 1` (exit 1), one key gave `ok` (exit 0), an unreachable host `FAIL: JWKS not reachable` (exit 1). The demonstration against the real staging authservice with the key removed is pending T-09.01.

## 7. Health checks: Fly checks for Fly (decision on the open question of PR #90)

The backend Dockerfiles carry a bash `/dev/tcp` `HEALTHCHECK` on `/alive` for plain Docker (CI `docker-build`). **Fly does not read a Dockerfile `HEALTHCHECK`**, so for Fly every app declares its own check in its `fly.toml`: authservice `GET /health/ready`; PostgreSQL TCP 5432; the backends `GET /alive` (liveness) and the web app `GET /api/health`, in T-09.02's per-app `fly.toml` files. The Dockerfile `HEALTHCHECK` stays for the CI image test and is not duplicated in Fly. The authservice image is upstream's, so it gets no `docker-build` row here.

## 8. Blocked and unverified

- Needs T-09.01 (Fly organisation, deploy token): creating the apps, volume, secrets, first deploy, the staging JWKS evidence, the token-accepted-by-Directory check and the assertion failing on the real app.
- `fly.toml` syntax (the `[[files]]` injection and a `[[services]]` block with no public port) was not validated by `fly config validate` (no CLI or account here). Verify on first deploy.
- The domain (hostnames) is undecided; every `<...-public-host>` is a placeholder.

## 9. The real authservice, proven against the product (T: real-authservice)

CI job `integration` (`.github/workflows/ci.yml`) runs the image pinned by digest (v0.3.4) on PostgreSQL 17 with RS256 and exercises it through the product's own pieces. Everything marked **proven** is a test that passes there; the same tests were also run on a machine without Docker against the v0.3.4 source built as a process (section 10), which is evidence about the tests, not about the image.

| Flow | Proof | Where |
|---|---|---|
| Consent versions, register `200` with tokens, wrong consent version / weak password / duplicate address `400 {errors}`, login, wrong password and unknown address the same `401`, lockout (5 wrong passwords, then the right one gets `401 {lockedOut, lockoutEnd}`, a guesser learns nothing), refresh rotation, replay revokes the whole family, empty refresh `400`, logout revokes refresh tokens, `/auth/me`, consents, export, delete account (needs `DELETE` and the password, then login and refresh are `401`), JWKS (one key) and discovery | one contract suite, run against **DevIdentity** (unit tier) and the **real image** (`AUTH_BASE_URL`) | `tests/Gabinety.Integration.Tests/AuthContract` |
| Register `202` without tokens when e-mail confirmation is required | proven (second instance, `Auth__RequireConfirmedEmail=true`) | `RealAuthFlowTests` |
| Role claim on the wire | observed and asserted: one role is a JSON **string** (`SuperAdmin` for the seeded `InitialAdmin`, `Admin` for an assigned one), two roles are a JSON **array** (`["Admin","User"]`), the long URI claim name, never `role`. No `iat`, `nbf`, `email_verified` | `RealAuthFlowTests` |
| Admin role reaching the services | proven: Directory, Booking and Reputation (in process, real JWKS over HTTP, `Identity__Authority` and `__Audience` only) answer `/api/v1/admin/*` with `401` without a token, `403` to a plain user and to the platform role `User`, `200` to `Admin`, to `SuperAdmin` and to a user with two roles | `RealAuthProductTests` |
| Directory's own client of authservice | proven: `HttpEmailVerifiedLookup` reads `emailConfirmed` from the real `/api/v1/auth/me` | `RealAuthProductTests` |
| Erasure (RUNBOOK section 8) | proven for a real account: authservice soft delete, then the three hooks (`204`, idempotent, refused without the internal key), Directory no longer finds the profile under the old id. Nothing orchestrates this in code: the owner calls the hooks by hand | `RealAuthProductTests` |
| Administrator-initiated erasure | proven: `GET /api/v1/admin/users?search=`, `DELETE /api/v1/admin/users/{id}`, `GET /api/v1/admin/users/deleted` | `RealAuthFlowTests` |
| Two-factor | proven: `200 {requiresTwoFactor:true, challengeToken, expiresIn:300}` with no token, `2fa/login` with a TOTP code, a wrong code `401`, a recovery code once | `RealAuthFlowTests`, and the web maps it to `two_factor_not_supported` |
| GitHub sign-in, **start only** | proven with dummy credentials: `302` to `https://github.com/login/oauth/authorize` with `client_id`, `state`, `redirect_uri=<authservice>/signin-github`; the web's own `returnUrl` (its origin, `/oauth/callback`, a query) is accepted; another origin or path, another provider, or an unconfigured instance is `400` | `RealAuthFlowTests`, `web/src/lib/session/real-authservice.test.ts` |
| Rate limit | proven: a burst from one address gets `429 {error, retryAfter}` | `RealAuthFlowTests` |
| Key rotation | proven: `Jwt__PrivateKeyPem` new, `Jwt__PreviousPublicKeyPem` old: the JWKS has two keys, a token signed before the rotation verifies against it and Directory accepts it, a new login is signed with the new `kid` | `RealAuthProductTests` |
| The Next.js web's handlers (`web/src/lib/session`, retired at the cutover) | **was proven** by `web/src/lib/session/real-authservice.test.ts` (Vitest); that test and the code left with `web/` | - |
| **The Blazor host's authentication** (`src/Gabinety.Web/Auth`, `/oauth/*`, `/konto`, `/admin`), in process (`WebApplicationFactory<Program>`) against the real image, through the host's own forms and cookies | proven: registration `200` signs in and `202` is the check-inbox state with no session; a taken address and a wrong password are one uniform message each (and an unknown address reads like a wrong password); five wrong passwords then the right one is the lock-out text, a guesser still gets the generic one; a burst from one address gets the "wait" text while a second visitor is served (the host forwards `Fly-Client-IP`); two-factor on its own page (wrong code keeps the challenge); the session cookie is `HttpOnly`, `SameSite=Lax`, host-only, `Secure` on https, holds no JWT and is under 3.5 KB for a plain user, a `SuperAdmin` (role as a string) and a two-role user (an array); a tampered, truncated or foreign-key-ring cookie is signed out; renewal of an access token about to expire from the cookie, with the rotated refresh token written back into the cookie; **eight concurrent requests of one session in a new process spend the single-use refresh token once**; a replayed refresh token ends the session and the family; logout revokes the refresh token at authservice and the same cookie is refused in this and in a later process; a token signed by a rotated key verifies after the host re-reads the key set; `/konto` (me, consents, export) and account deletion (wrong password refused, right one deletes and ends the session); the administrator area admits `SuperAdmin` (string), `Admin` (string) and `Admin`+`User` (array) and answers a plain user and the platform role `User` with the 403 page, a visitor with the sign-in redirect; the GitHub start (state cookie, `returnUrl`, authservice's redirect to github.com) and the callback's own refusals (no cookie, wrong state, a code authservice never issued) | `tests/Gabinety.Integration.Tests/BlazorAuth` (CI job `integration`; run against the v0.3.4 source as a process on a machine without Docker, section 10) |

### What is NOT proven

- **A real GitHub round trip** (the callback, the exchange code of a real sign-in, linking by e-mail): needs a real OAuth app (T-09.01). Only the start is tested; `POST /external-auth/exchange` is tested with a code that was never issued.
- **The callback of a real GitHub sign-in through the Blazor host** (`/oauth/callback` with a code authservice issued): the leg after GitHub needs a real OAuth app; only the host's refusals and the start are tested.
- **E-mail confirmation delivery and `POST /auth/verify-email`**: no mail sender exists (no SendGrid). The `202` is proven, the e-mailed link and the confirmation are not.
- **The AppHost default profile itself** (`dotnet run --project src/Gabinety.AppHost --launch-profile default`): it needs Docker and has never been started; no CI job runs it (section 11).
- The image on `linux/arm64`; behaviour behind Fly's proxy (forwarded headers, the `redirect_uri` origin).

### Findings that changed code or need a decision

1. **GitHub callback URL** is `<authservice>/signin-github` (section 5), not `/api/external-auth/callback`.
2. **`InitialAdmin` is a `SuperAdmin`**, not an `Admin` (`DbSeeder`). The product treats both as administrator, so it works; to get an `Admin` assign the role through `POST /api/v1/admin/users/{id}/roles` (SuperAdmin only; it also revokes that user's refresh tokens, so they sign in again).
3. **The `403 {emailVerificationRequired}` of login never appears** with `Auth__RequireConfirmedEmail=true`: ASP.NET Identity refuses the unconfirmed account first, and the answer is the generic `401 {error}` (observed). The web's `verification_required` branch for `403` is therefore dead; after a `202` the person sees the "check your inbox" text only at registration. A later sign-in attempt before confirming reads as "wrong e-mail or password". Fixing it needs authservice (or a different setting), not the web.
4. **Rate limit per client address** (decision of the supervisor, revisable). authservice limits `register`, `login`, `refresh`, `2fa/login`, the GitHub start and exchange to **20 a minute per client address** (queue of 5, then `429`); the numbers are hard-coded upstream (`Program.cs`), not configurable. The address is taken from `Network__ClientIpHeader` when set (else the connection's, which for the web app is the web app's own). Decision: authservice runs with `Network__ClientIpHeader=Fly-Client-IP` (fly toml and AppHost); public traffic gets that header from Fly's edge; the web (BFF) sends the visitor's validated address (`clientAddressOf`, never a raw header) in `Fly-Client-IP` **only on its server-to-authservice calls** (`web/src/lib/session/identity.ts`, login, register, refresh, exchange, consent versions; the Blazor gateway does the same, `IdentityClient`, and that is proven against the real image: two visitors get separate buckets through the host). Proven for the retired web: `web/src/lib/session/session.test.ts` (header forwarded, invalid values dropped, the visitor's own header never copied) and `real-authservice.test.ts` (one visitor exhausts the bucket and gets `429`, another is served at once). **To verify on the first deploy** (T-09.01): that Fly's edge overwrites a client-sent `Fly-Client-IP` on the public authservice app; if it does not, an outsider could name any address and sidestep the limit (the limit, not authentication, is what they would escape; lockout is per account and stays). Calls with a Bearer token on `/api/*` are limited per user, not address. The web also answers `429 rate_limited` (forms say to wait). A `429` on **refresh** still ends the session in the web (cookies cleared); reported, not changed.
5. **Password rules** (digit, lower-case, upper-case, special character, 8 or more): the web form and DevIdentity now enforce and show them; before, a plain 8-character password was refused with a generic error.
6. **DevIdentity differences fixed** (the stand-in must mimic reality): error bodies are `{error}`, the consent version is `2026-01-01` and must match, password rules, duplicate-address wording, lockout, `/consents`, `/export`, `DELETE /account` (soft delete), `/external-auth/providers`, an empty refresh token is `400`. Remaining differences are listed in `tools/Gabinety.DevIdentity/README.md`.
7. A soft-deleted account keeps its address: registering it again is `400` until authservice purges it (retention period unknown, RUNBOOK section 11 question 4).

## 10. Running the same tests without Docker (local verification only)

CI never uses this; the containers are the proof. On a machine with a PostgreSQL but no Docker you can run the tests against a published `AuthService.dll` of the same tag:

```bash
git clone --depth 1 --branch v0.3.4 https://github.com/konradcinkusz/authservice && cd authservice
dotnet publish src/AuthService -c Release -o /tmp/auth-out
export REALAUTH_LOCAL_DLL=/tmp/auth-out/AuthService.dll REALAUTH_LOCAL_PG="Host=localhost;Port=5432;Username=postgres;Password=..."
dotnet test tests/Gabinety.Integration.Tests --filter "FullyQualifiedName~RealAuth"
```

The fixture then starts the DLL as a process per instance (ports and databases of its own). The contract suite takes `AUTH_BASE_URL` of an instance started with `Network__ClientIpHeader=Fly-Client-IP` (and, for the web file, `InitialAdmin__*` and dummy `OAuth__GitHub__*`, see the `integration` job).

## 11. Why there is no `e2e-real-auth` job

A Playwright job with the real authservice, PostgreSQL, the four services and the web (register, sign in, `/moje/rezerwacje`; administrator, `/admin`) was considered and **not added**. It would need the AppHost default profile to run in CI: Docker-in-AppHost, PostgreSQL migrations of all four services (never run in this repository's CI), the first-ever start of that profile, and a cold first start, in a job of 15 to 25 minutes whose first run cannot be rehearsed here (no Docker). A red first run would teach nothing about authentication. What it would add beyond the tests of section 9 is only the wiring of the AppHost, which is a short, reviewed file. To run the journeys by hand against that profile: start it (`docs/GETTING-STARTED.md` section 7), then `E2E_DEMO_PASSWORD=Demo-Dev-Passw0rd! E2E_WAIT_FOR_SEEDER=0 pnpm --dir tests/e2e test -- --grep "J2"`; they register with a password that satisfies authservice's rules.
