# Security review (T-10.01, repository side)

Method: `konradcinkusz/architecture-standards` guide `security-review` (sections 1-10), SPEC section 11, CONTRACTS sections 0, 1, 3-6, 10.
Reviewed: `main` at `8dde92f` (before PR #110). Date of the review: 2026-11-18.
Scope: Directory, Booking, Reputation, Assistant, `Gabinety.ServiceDefaults`, `web/` (BFF and pages), `tools/Gabinety.DevIdentity`, `deploy/`, Dockerfiles, `.github/workflows`.

**Staging checks: N/A, not deployed.** The staging system does not exist yet (T-09.01 and T-09.08 are owner work). Everything that needs a running staging (headers actually served by Fly, TLS, the real authservice behaviour, `/internal/v1/test/*` answering 404 on the deployed app, CORS preflight against the public host) is listed in "Not reviewed" and must be repeated once staging exists.

Evidence labels: **[read]** = read in the code at the cited place; **[ran]** = a command was run and its output seen; **[test]** = an existing test that asserts it (the test was read, not re-run here: no .NET build of the solution was run for this docs-only change).

This is a static review. It does not replace a penetration test.

## 1. Summary

| Rank | Count | Findings |
|---|---|---|
| High | 1 | F-01 |
| Medium | 5 | F-02 to F-06 |
| Low | 7 | F-07 to F-13 |

High and Medium findings and their proposed tasks are listed in section 9 ("Tasks to create"). Launch (M3) is blocked by F-01 (SPEC section 11: any open `High`).

Time windows (guide section 3): High = P1 before M3 is claimed; Medium = P2 within a week of staging; Low = P3/P4.

### Status since the review (updated 2026-11-18, T-10.13 backlog pass)

| Finding | Status | Where |
|---|---|---|
| F-01 | **Fixed** (PR #117, merged): Mock fails closed outside the non-deployed profile | `Booking/Payments/Mock`, `MockPaymentsFailClosedTests` |
| F-02 | **Fixed** (PR #113, merged): nonce CSP, frame-ancestors, nosniff, referrer, permissions | `web/src/proxy.ts`; verify the served headers on staging |
| F-03 | **Fixed in code** (PR #116, merged): edge guard on `/internal/*`, per-caller keys, key strength at start, `Gabinety.ServiceDefaults.Edge`. **Open on staging:** the Fly header list is not verified against a deployed app (RUNBOOK 11, item 9) | `InternalAccess.cs` |
| F-04 | **Fixed** (PR #110, merged): table of CONTRACTS 10.2, user-id key after authentication, forwarded visitor address with `RateLimit__TrustedProxyKey`. Re-reviewed below; one gap closed in this PR | F-04 section |
| F-05 | not touched here (T-10.11, another session) | |
| F-06 | **Fixed** (PR #115, merged): fallback policy and the route table test | `docs/operations/ROUTE-AUTH.md` |
| F-07 | **Fixed** (this PR): OpenAPI only in the fake profile | `OpenApiExposure.cs` |
| F-08 | **Accepted per D-33** | |
| F-09 | **Partly fixed** (this PR): web renders https links only; the rest is an owner decision | |
| F-10 | **Config guard added** (this PR, template check); code change not made, reasons below | `scripts/check-private-topology.sh` |
| F-11 | **Fixed** (this PR): deployed profile refuses to start without audience and an https authority (services); web side as F-10 | `InternalAccess.ValidateIdentity` |
| F-12 | **Accepted pending owner confirmation** | |
| F-13 | **Accepted / owner**: nothing changed | |

## 2. Method and commands run

| What | Command | Result |
|---|---|---|
| Secret scan, full history (50 commits) | `gitleaks detect --source . --config .gitleaks.toml` (v8.18.4, default rules) | **[ran]** no leaks found |
| Secret-shaped strings, manual | `git ls-files` for `*.pem`, `.env`, `*.key`; grep for `BEGIN PRIVATE KEY`, `AKIA`, `ghp_`, `sk-`, `Password=` | **[ran]** no key files tracked; the only `Password=` hits are the CI-only throw-away value `ci-only` (`.github/workflows/ci.yml:116`), the Testcontainers default (`ThinSliceTests.cs:47`) and `<POSTGRES_PASSWORD>` placeholders in `docs/operations/AUTH.md` |
| .NET dependencies | `dotnet list GabinetyTech.sln package --vulnerable --include-transitive` (SDK 10.0.100) | **[ran]** no vulnerable package in any of the 23 projects |
| web production dependencies | `pnpm --dir web audit --prod` | **[ran]** no known vulnerabilities |
| web all dependencies | `pnpm --dir web audit` | **[ran]** 3 findings (2 high, 1 moderate), all dev tooling only (see F-13) |
| e2e dependencies | `pnpm --dir tests/e2e audit` | **[ran]** no known vulnerabilities |
| Redirect filter | `safeRedirect` from `web/src/lib/session/handlers.ts:123` run against 10 hostile inputs | **[ran]** every external or scheme target became `/`; `/%2F/evil.com`, `/..//evil.com`, `/@evil.com` stay as same-origin paths (checked: resolve to the site's own origin) |
| PR #110 read | `pull_request_read` on konradcinkusz/gabinety-tech#110 | **[read]** it changes the rate limits; this document describes, it does not duplicate |

## 3. Route table (every route of the five surfaces)

Policies are defined in each service's `Program.cs:11-14`: `Authenticated` (signed-in), `Admin` (role `Admin` or `SuperAdmin`), `Internal` (only scheme `InternalKey`, constant-time compare, `Security.cs:90-103`). **There is no fallback policy**: a route with no `RequireAuthorization` is public (see F-06).

"Ownership" = how the resource is tied to the caller. All were read.

### Directory

| Route | Policy | Ownership / note |
|---|---|---|
| `GET /api/v1/taxonomy`, `/practitioners`, `/practitioners/{slug}`, `/sitemap`, `/ranking-rules` | anonymous (explicit) | public data; `{slug}` returns `Approved` only (`SearchFeature.cs`) |
| `GET /api/v1/me` | Authenticated | claims of the caller |
| `POST/GET/PUT /api/v1/me/practitioner`, `POST .../submit` | Authenticated | row found by `user_id == sub` (`ProfilesFeature.cs`), no id in the route: no IDOR possible |
| `GET/POST/PUT /api/v1/me/practitioner/offerings`, `POST .../{id}/deactivate` | Authenticated | `OnOwnRow` + `x.Id == id && x.PractitionerId == p.Id` (`OfferingsFeature.cs:82-103`): a foreign offering id is 404 |
| `POST /api/v1/me/practitioner/github-link`, `.../verify` | Authenticated | own row |
| `GET /api/v1/admin/practitioners`, `/{id}`, `/{id}/verification-events`; `POST /{id}/approve`, `/reject`, `/suspend`, `/reinstate` | Admin | any admin, any practitioner (by design). No actor != subject check (F-08) |
| `PUT/DELETE /api/v1/admin/moderators/{userId}` | Admin and role `SuperAdmin` | `VerificationFeature.cs:109-110`; the `moderators` table is never read to grant anything (F-08) |
| `GET /internal/v1/offerings/{id}`, `/practitioners/by-user/{userId}`; `PUT /practitioners/{id}/reputation`, `/booking-stats`; `DELETE /subjects/{userId}` | Internal | see the caller matrix |
| `POST /internal/v1/demo/reset` | Internal, mapped only when `GABINETY_FAKE=1` or `Environment:Kind=Development` | section 5 |
| `GET /health`, `/alive`, `/health/details`, `/openapi/v1.json` | anonymous | F-07 |

### Booking

| Route | Policy | Ownership / note |
|---|---|---|
| `GET /api/v1/practitioners/{practitionerId}/slots` | anonymous (explicit) | public |
| `GET/PUT /api/v1/me/availability` | Authenticated | practitioner id resolved from Directory by the token's `sub` (`AvailabilityFeature.cs:52-65`); no id from the client |
| `POST /api/v1/bookings` | Authenticated | `client = sub`; `Idempotency-Key` required, record keyed `(client, key)` (`CreateBookingFeature.cs:47-58`); `own_offering` by user id (line 68) |
| `GET /api/v1/bookings/{id}` | Authenticated | client, practitioner or admin, else 404 (`BookingViewFeature.cs:36`); meeting URL only for a party of a `Confirmed` booking; fee/payout only practitioner and admin (`ToView`, lines 50-69) |
| `GET /api/v1/me/bookings` | Authenticated | `ClientUserId == sub` or `PractitionerUserId == sub`; the cursor is bound to role and status and cannot leave the caller's rows (`MyBookingsFeature.cs`) |
| `POST /api/v1/bookings/{id}/cancel` | Authenticated | client or practitioner of it, else 404 (`CancelFeature.cs:110`) |
| `POST /api/v1/bookings/{id}/dispute` | Authenticated | client only, 404 before the body is read (`DisputeFeature.cs:46-47`) |
| `POST /api/v1/bookings/{id}/report-client-no-show` | Authenticated | practitioner only (`ReportClientNoShowFeature.cs:104-105`) |
| `POST /api/v1/payments/simulate` | Authenticated | **no ownership check**; only refuses when the provider is not Mock (`PaymentWebhookFeature.cs:54-70`): F-01 |
| `POST /webhooks/payments/{provider}` | anonymous + signature | section 6 |
| `GET /api/v1/admin/disputes`, `POST /api/v1/admin/bookings/{id}/resolve`, `GET /api/v1/admin/ledger.csv` | Admin | |
| `GET /internal/v1/bookings/{id}/reviewability`, `DELETE /internal/v1/subjects/{userId}` | Internal | |
| `GET/POST /internal/v1/test/clock`, `POST /internal/v1/demo/reset` | Internal, non-deployed only | section 5 |
| `/health*`, `/openapi/v1.json` | anonymous | F-07 |

### Reputation

| Route | Policy | Ownership / note |
|---|---|---|
| `POST /api/v1/reviews` | Authenticated | Booking's reviewability must say `ClientUserId == sub` else 404; one review per booking (unique index) (`CreateReviewFeature.cs:44-46`) |
| `GET /api/v1/practitioners/{practitionerId}/reviews` | anonymous (explicit) | `Published` only; does not check the practitioner's status (F-12) |
| `POST /api/v1/reviews/{id}/reply` | Authenticated | practitioner id from Directory by `sub`; the UPDATE's WHERE has `PractitionerId == practitionerId` and `Published` and `ReplyText == null`, so a foreign review is 404 (`ReplyReviewFeature.cs:100-114`) |
| `GET /api/v1/admin/reviews`, `POST .../{id}/publish`, `.../{id}/reject` | Admin | the practitioner cannot reject (not an `Authenticated` route) |
| `DELETE /internal/v1/subjects/{userId}`, `GET/POST /internal/v1/test/clock`, `POST /internal/v1/demo/reset` | Internal (last two non-deployed only) | |
| `/health*`, `/openapi/v1.json` | anonymous | F-07 |

### Assistant

| Route | Policy | Ownership / note |
|---|---|---|
| `GET /api/v1/assistant/status` | no policy = anonymous (matches CONTRACTS 6) | |
| `POST /api/v1/assistant/brief` | Authenticated | own per-user limiter `BriefRateLimiter` (5/min, 20/day) (`BriefFeature.cs:197-206`) |
| `POST /internal/v1/assistant/review-triage` | Internal | |
| `/health*`, `/openapi/v1.json` | anonymous | F-07 |

### Web BFF and pages

| Route | Gate | Note |
|---|---|---|
| `/api/auth/login`, `/register`, `/logout`, `POST /refresh` | `checkOrigin` first (`handlers.ts:46-98`) | tokens only in `Set-Cookie`, never in a body (`finish`, line 25-31) |
| `GET /api/auth/refresh?redirect=` | none (GET) | touches only the caller's own cookies; target through `safeRedirect`, `/api/` refused (`handlers.ts:104-115`) |
| `GET /api/auth/session`, `/api/config`, `/api/health` | none | decode-only identity for display (`claims.ts:12`) or public config (no address, key, token) |
| `GET /api/auth/github/start`, `/callback` | none | `start` sets the `gt_oauth_state` cookie; `callback` refuses unless the `state` query equals it (F-05, fixed) |
| `/api/proxy/{directory,booking,reputation,assistant}/**` | `checkOrigin` (state-changing), then forwards the user's token from the cookie | segments validated (`safeSegments`), allow-list of 4 backends, `redirect: "manual"`, response `Set-Cookie` dropped (`proxy.ts:61-75, 114-116, 193`). It does not forward `X-Internal-Key` or `X-Payment-Signature` (allow-list `FORWARD_REQUEST`, line 48), so `/internal/*` and the webhook are not usable through it |
| `/admin/ksiega/csv` | middleware (signature) + cookie decode admin flag; Booking verifies again | filename built from validated dates (`csvFileName`) |
| `/konto/export` | cookie required; authservice authorises | |
| Pages `/moje`, `/gabinet-programisty`, `/admin`, `/rezerwacja`, `/checkout`, `/konto` | `proxy.ts` (edge middleware): verifies the **RS256 signature**, expiry, issuer and audience when configured (`guard.ts` `createVerifier`); `/admin` also the role | the matcher skips paths ending in an extension (`proxy.ts:22`); no protected page has one. Admin pages gate again in `app/admin/layout.tsx` (`requireAdmin`) |

## 4. Internal-key callers matrix (SPEC 5.1 against the code)

Mechanism: each callee has one `Internal:Key`; each caller holds the callee's key as `Internal:Keys:<callee>` (`Security.cs:95`, the client registrations). There is **one key per callee, shared by all of its callers**; the key is not tied to a caller and has no minimum length (F-03).

| Caller | Callee | Operations (read in the clients) | In SPEC 5.1? | Reachable beyond the SPEC with the same key |
|---|---|---|---|---|
| Booking | Directory | `GET offerings/{id}`, `GET practitioners/by-user/{userId}`, `PUT practitioners/{id}/booking-stats` | yes | all of Directory's internal routes: `PUT .../reputation`, `DELETE subjects/{userId}` |
| Reputation | Directory | `GET practitioners/by-user/{userId}`, `PUT practitioners/{id}/reputation` | yes | `PUT .../booking-stats`, `GET offerings/{id}` (meeting URL), `DELETE subjects/{userId}` |
| Reputation | Booking | `GET bookings/{id}/reviewability` | yes | `DELETE subjects/{userId}`, `POST practitioners/{id}/cancel-future-bookings` |
| Directory | Booking | `POST practitioners/{id}/cancel-future-bookings` (ADR-0012, on suspension and erasure; own key `INTERNAL_KEY_DIRECTORY_TO_BOOKING`) | **no: added by ADR-0012, SPEC 5.1 to be updated by the supervisor** | the other internal routes of Booking, as for any caller (F-03) |
| Reputation | Assistant | `POST review-triage` | yes | none other |
| Assistant | Directory | taxonomy | yes (public `GET /api/v1/taxonomy`, no key) | n/a |
| Web | any | none: no key in `web/` (`deploy/fly/web.fly.toml` comment; allow-list of forwarded headers) | yes ("Browser/web" rows are user-token calls) | n/a |
| owner runbook / seeder | all three | `DELETE subjects/{userId}`, `test/clock`, `demo/reset` | CONTRACTS 3.6, 4.3, 5.2, 10.4 | n/a |

No call exists in the code that SPEC 5.1 does not list. A holder of Booking's key to Directory can also reach the other caller's operations (privilege wider than needed): F-03.

## 5. Fake-profile endpoints (clock, reset, DevIdentity): proof they are unreachable in a deployed profile

- **Registration condition [read]:** `NonDeployedProfile.IsActive(config)` = `config["GABINETY_FAKE"] == "1"` or `Environment:Kind` equals `Development` (`Booking/Features/Internal/TestClock/TestClockFeature.cs:13-17`, same in Reputation and Directory `DemoResetFeature.cs`). `Map` returns before mapping when false; `Add` does not register the clock offset either. The condition reads configuration only (not `ASPNETCORE_ENVIRONMENT`), so `Production` or an unset variable gives 404.
- **Deployed config [read]:** `deploy/fly/{directory,booking,reputation,assistant}.fly.toml` do not set `Environment__Kind` or `GABINETY_FAKE` and say so (`booking.fly.toml:19`). `Environment__Kind=Development` is set only by `Gabinety.AppHost` (`Program.cs`, development composition root).
- **Tests [test]:** Booking `ClockApiTests` (`Outside_the_fake_profile_both_endpoints_are_not_mapped` with Production/other, `With_no_profile_settings_at_all_...`, `GET_clock_needs_the_internal_key_and_exists_only_outside_a_deployed_environment`), Reputation `ClockApiTests` (same names, flag `0` too), Directory `DemoResetApiTests` (`Outside_the_fake_profile_...`, `With_no_profile_settings_at_all_...`). Even when mapped, the routes need the internal key.
- **DevIdentity [read]:** `tools/Gabinety.DevIdentity/Program.cs:5-10` exits with code 1 unless `GABINETY_FAKE=1`; `ArchitectureRules.DevIdentity_is_isolated` [test] fails if any `src/` project references it or any Dockerfile mentions it.
- **Fake GitHub lookup [read]:** `VerificationFeature.Add` registers `FakeGithubProfileLookup` only under the same condition, so a deployed service uses the real lookup.
- **Residual:** the guarantee is configuration. The condition is one string compare away from being true in a deployed app. F-02 does not cover it; an integration test on the staging app is the second proof (CONTRACTS 10.4 already requires it, and it is not possible before staging).

## 6. Webhook, CSRF/Origin, idempotency

- **Webhook verification [read]:** HMAC-SHA256 hex over the raw body, `CryptographicOperations.FixedTimeEquals` (`MockPaymentProvider.cs:42-49`); body read raw and capped at 64 KB before parsing (`PaymentWebhookFeature.cs:94-106`); idempotent on `(provider, event_id)` (`PaymentEventHandler.cs:56`); amount and currency compared, mismatch recorded `Rejected` plus an `ALERT` log (`:60-66`); no secret and not Mock gives 401 (`:40`). OK for the structure. The only vendor adapter is the mock: whether a real vendor's signature scheme is handled is not testable now (T-09.05). F-01 is the problem that the Mock path is also the default.
- **Origin guard, services [read]:** `UseOriginGuard` (`Security.cs:70-88`) refuses a state-changing request with `Sec-Fetch-Site: cross-site` or a foreign `Origin`; first middleware in every `Program.cs:18`. Tests found: `Gabinety.Directory.Tests/KernelTests.cs` (the shared middleware), `web/src/lib/api/originGuard.test.ts`, e2e `j7-foreign-origin.spec.ts` [test, not re-run]. SPEC 5 rule 12 asks for a test in each service: only Directory's was found, the middleware itself is shared code. A request with neither header passes (a non-browser caller). Services accept bearer tokens, not cookies, so CSRF against them needs the token.
- **Origin guard, web [read]:** `checkOrigin` (`originGuard.ts:9-23`) applies to the proxy and to login, register, logout and POST refresh. Cookies are `SameSite=Lax`, so a cross-site POST carries no cookie anyway. GET routes do not change state except `GET /api/auth/refresh` (rotates the caller's own refresh token and redirects to a filtered path) and the GitHub callback (F-05).
- **Idempotency [read]:** `POST /bookings` as above; same key + other body = 409 (`Replay`, `:156-163`); the key space is per user so one user cannot replay another's response. Other writes are naturally idempotent or single-shot (reply: conditional UPDATE; review: unique booking; monotonic-version PUTs).

## 7. Rate limits (CONTRACTS 10.2): what main does; PR #110 is changing it

This section describes. It does not propose a duplicate task.

- **What main implements [read]:** a single global sliding window of 60 per minute (`Security.cs:54-65`), configurable by `RateLimit:PerMinute`; the partition key is `ctx.User.FindFirst("sub")` else `RemoteIpAddress`. `UseRateLimiter()` runs **before** `UseAuthentication()` in every `Program.cs` (`Directory/Program.cs:19-20`), so `ctx.User` is empty and the key is always the address. The Assistant says so in its own comment (`BriefFeature.cs`: "UseRateLimiter runs before UseAuthentication") and adds a per-user limiter only for `brief`. No code of the other rows exists on main: public reads 120/min, bookings 10/min and 30/day, reviews 5/hour, webhooks exempt.
- **Address [read]:** there is no `UseForwardedHeaders` anywhere (grep over `src`). `RemoteIpAddress` is therefore the TCP peer: for the browser path it is the web machine (every visitor shares one bucket per service), for direct calls to a public Fly listener it is the Fly proxy unless the platform rewrites it. This is the "every anonymous visitor shares one site-wide bucket" case that PR #110 names.
- **PR #110 [read]:** moves `UseRateLimiter` after authentication, adds the table of CONTRACTS 10.2 with a fake clock, exempts webhooks, and forwards the visitor address from the BFF in `X-Client-Address`, trusted only with a shared `RateLimit__TrustedProxyKey`.
- **Notes for the owner of #110, not a new task:** (a) after merge, the secret `RateLimit__TrustedProxyKey` must exist on 5 apps or the limiter degrades to the old shared bucket; (b) the services are directly reachable from the Internet (F-03), so a caller who skips the web app is keyed by the address the platform shows, and the per-minute budget of one address is not a defence against many addresses; (c) the guide's recurring blocker 3 ("rate limiting present in most services") applies until #110 lands. F-04 in this review is not duplicated here.

## 8. Rule-by-rule ledger (guide sections 4-9 and the task's list)

Legend: OK (evidence), N/A (reason and proof), FINDING (id). "Status" uses the guide's ledger words: FIXED/OPEN is only for findings.

### Guide 4: tokens in browsers

| Rule | Status | Evidence |
|---|---|---|
| No `localStorage`/`sessionStorage` for tokens | OK | **[ran]** `grep -rnE "localStorage|sessionStorage|document\.cookie|indexedDB" web/src` (non-test): no hit |
| Cookies set by a server route: `HttpOnly`, `Secure`, `SameSite` | OK with a Low note (F-10) | `cookies.ts:19-26`: `HttpOnly; SameSite=Lax; Path=/`, host-only, `Secure` when `Site__BaseUrl` is https. Tokens appear in no JSON body (`handlers.ts:25-31`, `identity.ts`) |
| BFF holds the token | OK | the browser calls `/api/proxy/*`; the proxy reads the cookie and adds the bearer header (`proxy.ts:133-156`) and drops `Set-Cookie` from backends (`:193`) |
| Gates verify the signature, issuer, audience | OK at the edge; F-11 for services | `guard.ts` `createVerifier`: `jwtVerify`, `algorithms: ["RS256"]`, issuer/audience when configured. Services: JwtBearer against the JWKS (`Security.cs:21-32`) |
| Header set on the frontend (`CSP`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`) | **FINDING F-02** | **[ran]** `grep -rniE "Content-Security-Policy|X-Frame-Options|frame-ancestors|Referrer-Policy|Permissions-Policy|nonce" web/src web/next.config.ts web/Dockerfile`: only `X-Content-Type-Options` on the CSV route (`admin/ledger/server.ts:36`); `next.config.ts` has only `output` and `poweredByHeader` |

### Guide 5: random values

| Rule | Status | Evidence |
|---|---|---|
| Anything presented as proof is a CSPRNG value of at least 256 bits | OK with one note | **[ran]** grep `Random|Guid.NewGuid|RandomNumberGenerator` in `src`, `tools`, `web/src`. Proof values: the GitHub challenge `gabinety-verify-` + `RandomNumberGenerator.GetString(36 chars, 10)` (`VerificationFeature.cs:78`, about 51.7 bits; it is a 30-minute, single-account, public-bio challenge that is only useful with the owner's own login, so the shortfall against 256 bits is acceptable here, recorded); DevIdentity refresh tokens 64 random bytes (`IdentityStore.cs:84`), salts `RandomNumberGenerator` (`:58`). `Guid.NewGuid()` is used only for row ids, `jti`, erasure placeholders, and the mock provider payment id `mock_<guid>` (`MockPaymentProvider.cs:18`): none is an access secret on a deployed path (a real provider assigns its own id) |
| Booking and other ids | OK | `Guid.CreateVersion7()` for bookings, as CONTRACTS; ids are not secrets and every route checks ownership, so guessing an id gives nothing (route table) |
| Password-hash parameters recorded | N/A for this repo | passwords are hashed by authservice, not here. The DevIdentity store is development only (`IdentityStore.cs`: salted hash, fake profile only). Proof: **[ran]** no `PasswordHasher`/`Rfc2898` use in `src/` |
| Signing key only in authservice | OK | `AddJwtAuthentication` has no key (`Security.cs:21-32`); `*.pem` is in `.gitignore`; gitleaks clean |

### Guide 6: user-supplied paths and names

| Rule | Status | Evidence |
|---|---|---|
| Proxy path | OK | `safeSegments` rejects empty, `.`, `..`, encoded dots/slashes, double encoding, control characters; backend must be one of 4 names; base from configuration (`proxy.ts:61-75, 114-130`); `redirect: "manual"` |
| File / CSV / export routes, `Content-Disposition` | OK | CSV filename comes from two `yyyy-MM-dd` values parsed to `DateOnly` on both sides (`AdminLedgerFeature.cs:47`, `ledger/server.ts:34` via `csvFileName`); account export uses a constant filename (`account.server.ts:64`); no route takes a file name from the user. **[ran]** no file-system read keyed by user input: the only `readFileSync` is `loadLegal` with a closed union type `LegalName` (`legal.ts:21`) |
| CSV injection | OK | ledger rows are a uuid, an enum name, integers, `PLN`, an ISO instant; no free text (`AdminLedgerFeature.cs:66`) |
| Slug handling | OK | `^[a-z0-9]+(-[a-z0-9]+)*$`, 3 to 60 (`SlugGenerator.IsValid`, `Rules.cs:45-48`); generated slugs only contain `[a-z0-9-]` (`FromName`); looked up as data, never used as a path; UI links use `encodeURIComponent` where the value is not a slug |
| Open redirect `?redirect=` | OK | `safeRedirect` (`handlers.ts:123-131`) and `safeReturnTo` (`returnTo.ts`): must start with `/`, not `//`, not `/\`, no control characters, resolves to the same origin, `/api/` and auth pages refused. **[ran]** hostile list in section 2. The GitHub `returnUrl` is built from `Site__BaseUrl`, not from the request host (`handlers.ts:139`). Tests: `redirectParam.test.ts`, `returnTo.test.ts` |
| Header injection | OK | no user value reaches a response header: `Location` is `new URL(...).toString()` of a filtered path; `Retry-After` from a backend is matched against `^[0-9A-Za-z ,:+-]{1,64}$` (`ledger/server.ts:48`); the proxy forwards only an allow-list of response headers, `location` included from a backend with `redirect: "manual"` (`proxy.ts:49`) |
| Meeting / portfolio URL rules | OK, with F-09 | https only, named host (no IP, no localhost), at most 300; meeting hosts allow-listed (`UrlRules`, `Rules.cs:70-84`). `https://meet.google.com@evil.com/` resolves to host `evil.com` (`Uri.IdnHost`) and is refused |

### Guide 7: output, errors and rendering

| Rule | Status | Evidence |
|---|---|---|
| React escaping, `dangerouslySetInnerHTML` | OK | **[ran]** `grep -rnE "dangerouslySetInnerHTML|innerHTML|eval\(|new Function|document\.write" web/src` (non-test): one hit, the JSON-LD script (next row) |
| JSON-LD in profile pages | OK | `serializeJsonLd` = `JSON.stringify(...).replace(/</g, "\\u003c")` (`profile/jsonLd.ts:29`) so no value can close the script or open a comment; data are display name, headline, offering text, https portfolio URL, validated GitHub handle, numbers. |
| Markdown in legal pages | OK | there is no markdown library: **[ran]** `grep -rnE "marked|remark|rehype|markdown-it|showdown|micromark" web/package.json web/src`: none. `renderBlocks` (`static/legal.ts`) builds React nodes with text children, so markup in the file is escaped. The input is the owner's file, not user text |
| Model output never rendered as HTML | OK | no HTML sink exists (row above); the brief suggestion is a schema-validated object (`BriefFeature.cs` 150-170) |
| Errors do not leak internals | OK | RFC 9457 bodies with codes; proxy returns `dependencyUnavailable()` without the error (`proxy.ts:181-184`); login answers a uniform 401/400 (`handlers.ts:25-44`). Exceptions are logged by type only (below) |
| Swagger/OpenAPI off in production | **FINDING F-07** | `app.MapOpenApi("/openapi/v1.json")` is unconditional in 4 `Program.cs` (`:24`, `Assistant :30`); CONTRACTS 0 says "in development and in CI" |
| Encode at render, not storage | OK | free text is stored as typed (`Apply`, `ProfilesFeature` trims only) and rendered by React |

### Guide 8: authorization structure

| Rule | Status | Evidence |
|---|---|---|
| Deny by default, audited mechanically | **FINDING F-06** | no `FallbackPolicy`, no test that lists the route table (`Program.cs:11-14`). The anonymous list today is short: public reads, slots, reviews list, assistant status, health, openapi, the webhook |
| Endpoint × role matrix | produced | section 3 |
| No identity by e-mail comparison | OK | **[ran]** `grep -rniE "email ==|Email\.Equals|== .*email" src`: authorization uses `sub` and roles only (`UserId` everywhere) |
| Ownership at the resource (IDOR) | OK, one gap noted | section 3: booking detail 404 for strangers, review reply bound to the practitioner id in the WHERE, offering edit bound to the caller's row, admin routes behind `Admin`. Tests per endpoint with a second user are required by SPEC 11; not re-run. The gap is `payments/simulate` (F-01) |
| Admin routes | OK | every `/api/v1/admin/*` is in an `Admin` group; moderator management also needs `SuperAdmin` (`VerificationFeature.cs:109-110`) |
| What is never exposed | OK | public practitioner JSON has no e-mail, phone, NIP or meeting URL (`ProfileWire`, `SearchFeature.cs`); reviewer shown without a name (the `ReviewDto` has no reviewer field); meeting URL only to parties of a `Confirmed` booking |
| CORS | OK / N/A | **[ran]** `grep -rnE "AddCors|UseCors" src web`: none. No CORS means browsers cannot call the services cross-origin, and the web app is same-origin; a wildcard PaaS origin is therefore not present. When the public listeners become private (F-03) CORS stays unused |
| Recurring blocker 1: production CORS origins | N/A | no CORS configured at all (above) |
| Recurring blocker 2: e-mail confirmation | N/A here | authservice setting. CI runs the authservice with `Auth__RequireConfirmedEmail=false` (`ci.yml:117`); the staging value is in `deploy/fly/authservice.fly.toml` and must be checked there at T-09.08 (not reviewed: needs staging) |
| Recurring blocker 3: rate limiting partial | FINDING F-04 / PR #110 | section 7 |
| Recurring blocker 4: token storage consistent | OK | one frontend; cookies only |

### Task list items not covered above

| Item | Status | Evidence |
|---|---|---|
| SSRF (server-side fetch of user URLs) | OK | **[ran]** every `HttpClient`/`fetch` target in `src`, `web/src`, `tools`: all are `Backend__*`/`Identity__*` configuration, or GitHub with a fixed host and a regex-validated handle (`GithubLookup.cs:24,44-51`: `^[A-Za-z0-9]([A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z`). The meeting URL and the portfolio URL are stored and rendered as links, never fetched. The web app renders no remote image (**[ran]** no `<img>`/`next/image` in `web/src`), so no image host list is needed |
| Secrets in repo | OK | section 2 |
| Dockerfiles and workflows: secret exposure | OK with F-13 | all 5 images run as non-root (`USER app` / `USER node` / `USER postgres`), no `ARG`/`ENV` carries a secret (**[ran]** grep), the web image does not copy anything but build output and `content/` (`web/Dockerfile:25-27`), `scripts/check-docker-image.sh` asserts non-root in CI. Workflows: `permissions: contents: read`, no `pull_request_target`, no secret other than `GITHUB_TOKEN`; the CI authservice key is generated per run (`ci.yml:112`) |
| `deploy/` templates | OK with F-01/F-03 | no secret value in any `*.fly.toml`; secrets are listed by name only; `Environment__Kind`/`GABINETY_FAKE` deliberately absent; Postgres has no public service (`postgres.fly.toml:27`). But `Payments__Provider = "Mock"` and public listeners: F-01, F-03 |
| Logging of PII and free text | OK | **[ran]** every `Log*(` call in `src`/`tools` read: arguments are ids, statuses, counts, lengths, exception **types** (`log.LogWarning("... ({ExceptionType})")`); the three `LogError(ex, ...)` are migration and taxonomy-seed failures; the notification failure reason is `"the sender failed: " + ex.GetType().Name` (`NotificationDispatcher.cs:84`); `LogEmailSender` logs the user id and the subject **length** (`Email.cs:32`). Mechanical guard: `ArchitectureRules.No_free_text_in_log_calls` [test]. Web: only `console.warn` of a status code (`ledger/server.ts:27,41`) |
| Dependencies | OK | section 2 |
| Fake-profile endpoints | OK | section 5 |
| Webhook verification | OK | section 6 |
| CSRF/Origin (J7) | OK | section 6 |
| Idempotency keys | OK | section 6 |
| Prompt injection (SPEC 11 threat 4) | OK | brief and triage: no tools, the user text follows a fixed delimiter and any copy of the delimiter in it is removed (`BriefFeature.cs:132`, `TriageFeature.cs:101`), output is schema-validated with one repair attempt where the previous reply is fenced as data (`RepairPrompt`), the result is advisory |
| XXE, NoSQL, deserialisation | N/A | **[ran]** `grep -rniE "XmlReader|XmlDocument|XDocument|Mongo|BinaryFormatter|TypeNameHandling" src tools web/src`: no hit; JSON via System.Text.Json only. If XML is ever added: `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }` |
| SQL injection | OK | EF Core with parameters; the one raw SQL is `FromSql($"... WHERE user_id = {uid} FOR UPDATE")` which is parameterised by the interpolated-string overload (`OfferingsFeature.cs:56`); `q` is searched with `.Contains` (escapes `%`/`_`) |

## 9. Positive findings (do not re-check)

1. Every user-owned route resolves the resource from the token's `sub` or checks it in the query, and answers 404 (not 403) to a stranger.
2. Webhook handling: raw body, constant-time HMAC, 64 KB cap, event-id idempotency, amount/currency check.
3. No token in browser storage; cookies HttpOnly + SameSite; the BFF never forwards cookies or an internal key; response `Set-Cookie` from backends is dropped.
4. Edge middleware verifies the RS256 signature (not a decode).
5. Redirect parameters, proxy paths, CSV names and JSON-LD are filtered or escaped at the right place; no HTML sink for user or model text.
6. Logs carry no free text, with a mechanical test.
7. Secret scan, .NET and production pnpm audits are clean.
8. Fake-profile endpoints and DevIdentity are gated by configuration, absent from the deployed config, and tested.

## 10. Findings

Format of the guide: Location, Issue, Current code, Risk, Attack scenario, Impact, Recommendation, Better. Proposed task ids are placeholders (`T-10.08` onward) for the supervisor to assign.

### F-01 Mock payment provider is the default, is accepted unsigned, and can be driven by any signed-in user

- **Location:** `src/Gabinety.Booking/Features/Payments/PaymentWebhookFeature.cs:28,40,54-70`; `src/Gabinety.Booking/Payments/Mock/MockPaymentProvider.cs:28,55-60`; `deploy/fly/booking.fly.toml:25`; `deploy/fly/web.fly.toml:20`.
- **Issue:** nothing ties "Mock" to a non-deployed environment: `Payments:Provider` defaults to Mock when unset, the deployed templates set it to Mock, the mock accepts any webhook body when no secret is set, and `POST /api/v1/payments/simulate` lets any signed-in user mark **any** booking's payment succeeded or failed (it checks the provider, not who owns the booking).
- **Current code:** `if (!IsMock(payments)) return Problem(... Forbidden ...)` then `db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == req.BookingId, ct)` with no comparison to the caller (`PaymentWebhookFeature.cs:57-65`); `if (!string.IsNullOrEmpty(webhookSecret) && !SignatureMatches(...)) return null;` (`MockPaymentProvider.cs:28`).
- **Risk:** **High** (blocks M3). Verified by reading; the Booking app is a public Fly listener (`booking.fly.toml:34`, "the whole app answers on <app>.fly.dev").
- **Attack scenario:** (a) a client books a 600 PLN slot, then calls `POST /api/proxy/booking/api/v1/payments/simulate {bookingId, outcome:"succeeded"}`: the booking becomes `Confirmed`, the practitioner's meeting URL is released, no money moved. (b) A hostile user who learns another client's booking id (it appears in the confirmation URL and in e-mails) sends `outcome:"failed"` and the victim's pending booking fails, freeing the slot for the attacker. (c) If the production deploy ever forgets `Payments__Provider` or the real adapter's name, the service silently runs Mock.
- **Impact:** payment bypass and booking sabotage; fee evasion; fake-review farming without cost (a review needs only a "paid" completed booking).
- **Recommendation:** refuse to start with Mock unless `NonDeployedProfile` is active or an explicit `Payments:AllowMock=true` is set (staging may opt in, production never); require `Payments:Provider` explicitly outside the non-deployed profile; in `simulate` require `booking.ClientUserId == sub` (404 otherwise); require `Payments:WebhookSecret` for Mock in a deployed profile.
- **Better:** put simulate and the mock webhook behind the non-deployed profile like the clock endpoint, and make the real adapter (T-09.05) the only provider that deployed code can select.
- **Proposed task:** T-10.08.
- **Status (2026-11-18): FIXED, PR #117 merged.** Mock fails closed outside the non-deployed profile; see `MockPaymentsFailClosedTests`.

### F-02 The web app sends none of the SPEC 11 security headers

- **Location:** `web/next.config.ts:3`, `web/src/proxy.ts` (no header logic), `web/Dockerfile`.
- **Issue:** no `Content-Security-Policy` (nonce, no `unsafe-inline` for scripts), no `frame-ancestors 'none'` / `X-Frame-Options`, no `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy` on any page. SPEC 11 and guide 4 require them.
- **Current code:** `const config: NextConfig = { output: "standalone", poweredByHeader: false };`
- **Risk:** **Medium**. Defence in depth: no XSS sink was found, so this is not exploitable by itself today.
- **Attack scenario:** an attacker frames `/rezerwacja/...` or `/moje/rezerwacje/{id}` on another site and overlays it (clickjacking a cancel or "Zapłać" click); and if any future XSS slips in, nothing restricts script origin or exfiltration, which is how a cookie-less BFF token could still be used through the same-origin proxy.
- **Impact:** clickjacking of signed-in actions; no containment of a future injection; referrer leaks the full URL (booking ids) to the GitHub and meeting links.
- **Recommendation:** set the five headers for every response in `proxy.ts` (CSP with a per-request nonce, `frame-ancestors 'none'`, `nosniff`, `strict-origin-when-cross-origin`, a closed `Permissions-Policy`), and a unit test per header; verify on staging.
- **Better:** a header test in the e2e job that fails when a header is missing on `/`, a profile page and `/moje`.
- **Proposed task:** T-10.09.
- **Status (2026-11-18): FIXED, PR #113 merged** (nonce-based CSP, `frame-ancestors 'none'`, `nosniff`, referrer policy, permissions policy in `web/src/proxy.ts`). Still to do: check the headers actually served by Fly on staging (section 12).

### F-03 Internal endpoints are internet-reachable behind one shared key per callee

- **Location:** `deploy/fly/{directory,reputation,assistant}.fly.toml` `[http_service]`; `src/Gabinety.ServiceDefaults/Security.cs:93-102`; section 4.
- **Issue:** only the Booking webhook needs a public listener, but all four services have one (Fly exposes the whole app). Internal routes (`/internal/v1/...`: reputation push, booking stats, erasure, offering lookup with the meeting URL) are protected only by `X-Internal-Key`, one value per callee shared by every caller, with no minimum length or format check, and no per-caller identity. The rate limiter keys the address of the platform proxy (F-04).
- **Current code:** `var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));` against `config["Internal:Key"]`.
- **Risk:** **Medium**.
- **Attack scenario:** an attacker from the Internet guesses or obtains one key (a short key in a log, a developer machine, a compromised Booking build) and calls Directory `PUT /internal/v1/practitioners/{id}/reputation {ratingCount:999, ratingAvg:5, version:9999999}` to set any ranking, or `DELETE /internal/v1/subjects/{userId}` to wipe a practitioner. A caller that legitimately holds Booking's key to Directory (needs only offerings and booking-stats) can do the same.
- **Impact:** ranking manipulation (the central threat of SPEC 11), account erasure, meeting-URL disclosure.
- **Recommendation:** make Directory, Reputation and Assistant private (no `[http_service]`; reach them by `.internal`; the web app stays public, Booking public only for the webhook path if Fly allows, else a separate tiny webhook app); refuse to start when `Internal:Key` is under 32 characters outside the non-deployed profile.
- **Better:** one key per caller-callee pair (`Internal:Keys:<caller>` on the callee) and the scheme records the caller as a claim; per-route policy "Internal:Booking" so a leaked Booking key cannot call Reputation's route.
- **Proposed task:** T-10.10.
- **Status (2026-11-18): FIXED IN CODE, PR #116 merged** (edge guard for `/internal/*`, one key per caller and callee, key strength and distinctness checked at start, in the new project `src/Gabinety.ServiceDefaults.Edge`). **Fly header check still needed on staging:** the list of headers that mark a request from the public edge (`Internal:EdgeHeaders`) was not verified against a deployed app.

### F-04 Rate limits of CONTRACTS 10.2 are mostly not implemented and the key is always an address

- **Location:** `src/Gabinety.ServiceDefaults/Security.cs:54-65`; `src/Gabinety.{Directory,Booking,Reputation,Assistant}/Program.cs:19-20`.
- **Issue and scenario:** see section 7. In short: one 60/min bucket per address; `UseRateLimiter` before `UseAuthentication`; no forwarded-address handling.
- **Risk:** **Medium**.
- **Attack scenario:** through the BFF, all visitors share the web machine's address, so one script sending 61 requests a minute to `/api/proxy/directory/...` makes the public reads fail for everyone (429); `POST /bookings` and `POST /reviews` have no per-user limits at all.
- **Impact:** cheap denial of service; unbounded booking creation and review submission per user.
- **Recommendation / status:** **covered by PR #110 (T-10.03). No new task.** Re-review after it merges: the order of middleware, the `RateLimit__TrustedProxyKey` secret on 5 apps, and that the direct public listeners (F-03) do not bypass it.
- **Status (2026-11-18): FIXED, PR #110 merged** (limits of CONTRACTS 10.2, user-id key, forwarded visitor address). Re-review below.

**Re-review of F-04 after PR #110** (read of the merged code `RateLimits.cs`, `Security.cs`, the four `Program.cs`, and the tests; the whole suite was run, not only the tests cited).

| CONTRACTS 10.2 row | Implementation | Test that proves it |
|---|---|---|
| Public reads, 120 a minute | `RateLimitTable`, group `reads` | Reputation `RateLimitTests.Public_reads_allow_120_a_minute_...`; Booking `...reads_of_the_slots_are_public_reads_with_their_own_budget`; the classification of every public read path in `RateLimitTableTests` |
| `POST /bookings`, 10 a minute and 30 a day per user | group `bookings`, two windows | Booking `RateLimitTests.Bookings_allow_10_a_minute...` and `...30_a_day...` |
| `POST /assistant/brief`, 5 a minute and 20 a day per user | the Assistant's own `BriefRateLimiter` (the table calls it "other") | `BriefTests` (6th call in a minute, 21st of the day, another user unaffected) |
| `POST /reviews`, 5 an hour per user | group `reviews` | Reputation `Posting_reviews_allows_5_an_hour...`, `A_refused_review_post_is_not_counted...` |
| Webhooks, none by address, 64 KB | group `webhooks` is unlimited; the 64 KB cap is the endpoint's | Booking `Webhooks_are_never_limited_by_address`; `PaymentWebhookTests` for the cap |
| Everything else, 60 a minute | group `other` | Reputation and Booking `Everything_else_...`; Directory `KernelTests.Rate_limit_answers_429...` |
| Key is the user id when signed in, else the address | `PartitionKey` | Reputation `A_signed_in_reader_is_keyed_by_the_user_id...`, `A_signed_in_user_keeps_the_user_id_key_whatever_address_is_forwarded` |
| Forwarded visitor address | `AddressOf`: only with the shared key (constant time) and exactly one IP | Reputation tests for no key, wrong key, spoof, a list, a port; web `proxyAddress.test.ts` |
| Strength of `RateLimit:TrustedProxyKey` | `KeyValidationService` | `InternalAccessTests.A_weak_or_shared_trusted_proxy_key_stops_the_start` |

- **Middleware order:** `UseAuthentication` then `UseRateLimiter` in all four `Program.cs` (read). Gap found: only Booking and Reputation proved the order by behaviour; Directory and Assistant had no test. **Closed in this PR:** `ArchitectureRules.The_rate_limiter_runs_after_authentication_in_every_service` (checked by swapping the two lines in a copy: it fails).
- **Gap, not closed (needs a deployed app or a decision):** no test builds Directory's or the Assistant's own host and spends the 120-a-minute read budget; they run the same shared code and the classification test covers their paths, so the risk is low.
- **`RateLimit__TrustedProxyKey` is optional by design** (`deploy/fly/web.fly.toml`). Unset on any one of the five apps, that backend silently degrades to one bucket for every visitor behind the web machine (the F-04 attack scenario returns for that backend). Nothing warns at start. Staging check: the secret exists on the web app and on all four backends.
- **Direct listener (F-03):** Booking is the one public backend. A direct anonymous caller is keyed by `RemoteIpAddress`; whether Fly rewrites it was not verified (section 12). Direct anonymous callers share that bucket, which does not touch the web app's visitors (they carry the forwarded address). Authenticated direct callers are keyed by user id.
- **Per instance:** the limiter is in memory; two machines of one app double every limit (CONTRACTS 10.2 says "per instance"). Booking runs the jobs with min 1 machine, so the figure holds today.

### F-05 GitHub sign-in callback is not bound to the browser that started it

- **Status: fixed in T-10.11 (this repository). Verified against authservice v0.3.4 (commit `dfcce91`, read-only clone, 2026-11-18).**
- **Location:** `web/src/lib/session/handlers.ts` (`githubStart`, `githubCallback`), `web/src/lib/session/cookies.ts` (`gt_oauth_state`).
- **What authservice does [read]:** it generates and validates the OAuth `state` of the GitHub leg itself (the ASP.NET `AddGitHub` handler and its correlation cookie, `Program.cs:231-237`, `ExternalAuthController.cs:87` `Challenge`, `:102` `GetExternalLoginInfoAsync`). That protects only GitHub -> authservice. The leg authservice -> web is `redirect <returnUrl>&code=<single-use code>` (`ExternalAuthController.cs:255,274`), and `POST /external-auth/exchange` redeems the code for tokens with no browser binding (`:293` `RedeemAsync(code)`; no cookie, no state, no PKCE). Whoever holds the code gets that user's session. So the gap was real and in our callback: the web handler accepted any `?code=`.
- **Attack scenario (unchanged):** the attacker runs the flow with their own GitHub, keeps the callback URL with their `code`, lures the victim to it (top-level GET, `SameSite=Lax` does not stop it); the victim's browser then holds the attacker's session.
- **Fix:** `githubStart` sets `gt_oauth_state` (32 random bytes, base64url, HttpOnly, SameSite=Lax, Secure on https, host-only, `Max-Age=600`) and adds the same value as `state` to the `returnUrl` query (authservice keeps the query and appends `&code=`). `githubCallback` refuses (no exchange, no session, `/logowanie?error=github_failed`) unless the query `state` equals the cookie (constant time, empty never matches), and every outcome deletes the cookie. The value is not signed: it is random and only ever compared with the cookie of the same browser, so a signature adds no property (a forged cookie needs the victim's browser; the host-only cookie cannot be set from another site).
- **Tests:** `web/src/lib/session/session.test.ts` (`describe("github")`): cookie attributes and randomness, state in `returnUrl`, one-shot deletion, refusal for missing cookie / missing state / different / empty / other-length state (no backend call), open-redirect targets on start and callback. Three mutations (callback skips the check, comparison always true, empty accepted) each fail the suite. DevIdentity has no external-auth endpoints, so the e2e journeys J1-J7 do not touch this flow; the CI e2e job is the proof they still pass.
- **Residual, not ours:** authservice has no per-browser binding of the exchange code; an authservice-side fix would be defence in depth. Question for the authservice maintainers: "Can `external-auth/callback` bind the exchange code to the browser that started the flow (a `state` echoed back, or a cookie on the authservice origin checked at `exchange`), or accept an optional client-supplied `state` that is returned unchanged and required at `exchange`?"
- **Also found and fixed here:** authservice accepts a `returnUrl` only on an allowed origin with path `/oauth/callback` or below it (`ExternalAuthController.cs:344-352`, 400 at `:62`), but we sent `/api/auth/github/callback`, so the real v0.3.4 would have refused GitHub sign-in at start. The callback handler now lives at `web/src/app/oauth/callback/route.ts` and `githubStart` sends `<Site__BaseUrl>/oauth/callback?[redirect=..&]state=..`; a test encodes the authservice rule. The other authservice paths we call (`/api/v1/auth/login|register|refresh|logout|consents/versions`, `/api/v1/external-auth/exchange`) match the spike's table. Deployment: `OAuth__PostLoginRedirectBaseUrl` / `__PostLoginRedirectAllowedBaseUrls__0` must be the web origin (AUTH.md).
- **Risk before the fix:** **Medium**; after: Low.

### F-06 No deny-by-default and no mechanical audit of the route table

- **Location:** `Program.cs:11-14` of the four services (`AddAuthorizationBuilder()` without `SetFallbackPolicy`); `tests/Gabinety.Architecture.Tests` (no route test).
- **Issue:** a route mapped without `RequireAuthorization` is silently public. Every route today is correct (section 3), but nothing keeps it so, and the guide's rule is mechanical.
- **Risk:** **Medium** (preventive; no current exposure).
- **Attack scenario:** a feature task adds `endpoints.MapPost("/api/v1/admin/x", ...)` and forgets the policy; the route works in every unit test that sends a token and is public in production.
- **Impact:** an unauthenticated admin or user-data route.
- **Recommendation:** `.SetFallbackPolicy(RequireAuthenticatedUser)` in `AddServiceDefaults`-level code, with `.AllowAnonymous()` on the explicit public list (already present on most), plus an integration test per service that enumerates `EndpointDataSource`, and compares the anonymous endpoints with a committed list (this document's section 3).
- **Proposed task:** T-10.12.
- **Status (2026-11-18): FIXED, PR #115 merged** (fallback authorization policy in every `Program.cs`; route table in `docs/operations/ROUTE-AUTH.md` checked by `RouteAuthTests` in each service).

### F-07 OpenAPI and `/health/details` are public on deployed services (Low)

- **Location:** `Program.cs:24` (`Assistant:30`); `Features/Health/HealthFeature.cs:22-31`.
- **Issue / scenario:** anyone can fetch the full route map (including `/internal/v1/*`, `/api/v1/admin/*`), DTO shapes and validation rules; `/health/details` names the dependencies and their state. Guide 7 asks for it to be off in production; CONTRACTS 0 says development and CI.
- **Risk:** Low. **Recommendation:** map OpenAPI only when `NonDeployedProfile` or `Environment:Kind=CI`; keep `/health` and `/alive`. Proposed task: T-10.13 (with F-06; Low, not blocking).
- **Status (2026-11-18): FIXED in this PR (T-10.13).** `OpenApiExposure.IsServed(config)` (new, in `src/Gabinety.ServiceDefaults.Edge`, so the 400-line kernel did not grow): `/openapi/v1.json` is mapped only when `GABINETY_FAKE=1` or `Environment:Kind=Development`, the same rule as the other fake-profile routes. The AppHost sets `Environment__Kind=Development` on every service, so the document stays available locally and in CI (`web/scripts/gen-types.mjs`). Tests: `OpenApiExposureTests` and, per service, `RouteAuthTests.The_OpenAPI_document_is_served_in_the_fake_profile_and_is_404_when_deployed`; the ROUTE-AUTH rows are now profile `fake` (the route table test fails if the guard is removed: tried). Since PR #116 the document was already 404 from the public edge of Booking and the other three apps have no public listener; this closes the private-network exposure. **`/health/details` is left as it is** (accepted): it only names the dependencies and their state, and it is reachable only over the private network.

### F-08 Admin routes have no separation of duties; moderators are inert (Low)

- **Location:** `VerificationFeature.cs:176-203` (`Transition`), `:256-278`, `MeFeature.cs`.
- **Issue:** an administrator who is also a practitioner can approve their own profile (no `actor != subject` check); the `moderators` table is written (`PUT /admin/moderators/{userId}`) but never read: `isModerator` is hard-coded `false` and moderators gain no right. Anyone adding a moderator expecting a limited role gets nothing; anyone assuming moderators are the only reviewers is wrong, every `Admin` can act.
- **Risk:** Low. **Recommendation:** refuse approve/reject/suspend on the caller's own practitioner row; either remove the moderator routes or make the `Admin`-group policy read the table. Proposed task: T-10.14.
- **Status (2026-11-18): ACCEPTED per D-33 (supervisor, 2026-11-18: self-approval by the single `Admin` stays as is).** Evidence: DECISIONS D-33 (2026-11-13, accepted by the owner): "The owner, as authservice `Admin`. No other moderators in the MVP; the moderator role exists in Directory for later." So the `moderators` table being inert is the decision, not a defect, and the owner is the only `Admin`: a rule "an admin cannot approve their own profile" would lock out a solo operator who also offers consultations (the likely bootstrap). Not changed. **Revisit when a second `Admin` or the first moderator is appointed:** then add the `actor != subject` refusal in `Transition` (about 3 lines, 403 `forbidden`) and make the `Admin` group read `moderators`, in one task. Documented in CONTRACTS terms: `isModerator` stays `false` until then.

### F-09 Approved profile text, portfolio URL and default meeting URL can change without a new review; portfolio URL is not scanned (Low)

- **Location:** `ProfilesFeature.cs:93-139` (`Update`): only a changed NIP resets the status; `Rules.cs:30-38` detector is applied to headline and bio only; `ProfilePage.tsx:44` renders `p.portfolioUrl` without checking the scheme in the web layer.
- **Scenario:** a practitioner is approved with a harmless portfolio, then changes it to `https://wa.me/48...` (a contact leak) or to a phishing page; the admin checklist "portfolio link opens" is checked once.
- **Risk:** Low (the server enforces https and a named host; contact detail leakage is a business risk, SPEC 6.1). **Recommendation:** run the detector on the portfolio URL; consider re-queue (`Submitted`) on a change of portfolio URL; in the web component render the link only for `https:`. Proposed task: T-10.15.
- **Status (2026-11-18): PARTLY FIXED in this PR (T-10.15), the rest is an OWNER DECISION (not implemented).**
  - Fixed (web): the profile page and the JSON-LD `sameAs` render the portfolio link only when it is `https:` (`safeHttpsUrl`, `web/src/components/profile/safeUrl.ts`; tests in `safeUrl.test.tsx`, which fail when the page uses the raw value: tried). The server already refuses anything else; this is the second check for a row written by another path.
  - Not changed: product/policy choices. CONTRACTS 3.4 (`PUT /me/practitioner`, `verification_events`) requires a re-queue only for a changed `nip`, so none is required for a portfolio change. Options and cost: (1) *the contact detector on the portfolio URL.* **[ran]** the phone pattern on a URL: `https://wa.me/48600100200` matches (wanted), but so do `https://www.linkedin.com/in/jan-kowalski-123456789/` and `https://demo.example/portfolio/2024-05-12` (false positives on ordinary portfolio links). A blocklist of messenger hosts (`wa.me`, `t.me`, ...) would be the precise alternative; that is the owner's call (SPEC 6.1 calls the detector anti-leakage, not security). (2) *Re-queueing an Approved profile when the portfolio URL changes* needs a new state-machine trigger (as `NipChanged`) and a CONTRACTS 3.4 change; the admin checklist "portfolio link opens" is today checked once. Cost: (1) a few lines and a host list in config, with the risk of blocking legitimate links; (2) a new state-machine trigger, a `verification_events` kind (CONTRACTS 3.1 enumerates them), migration and tests, and every portfolio edit of an approved profile leaves the public listing until an admin re-approves. Suggested if wanted: the blocklist plus re-queue on a changed portfolio host only.

### F-10 Cookie `Secure` depends on `Site__BaseUrl` and the refresh cookie path is `/` (Low)

- **Location:** `web/src/lib/session/cookies.ts:19-26`; `web/src/lib/config/index.ts:20`.
- **Issue:** `Secure` is set only when the site URL starts with `https://`; the default is `http://localhost:3000`. The refresh cookie (7 days) is sent to every path, including `/api/proxy`, where it is not needed.
- **Scenario:** a deploy that forgets `Site__BaseUrl` runs with non-Secure cookies on an https site and with an origin guard that compares to the request URL only.
- **Risk:** Low. **Recommendation:** in production (`NODE_ENV=production`) refuse to start without an https `Site__BaseUrl`; give the refresh cookie `Path=/api/auth`; consider the `__Host-` prefix. Proposed task: T-10.16.
- **Status (2026-11-18): NOT FIXED IN CODE, guarded in the templates (this PR, T-10.16).**
  - `Secure` and `https`: `scripts/check-private-topology.sh` (already a CI step, and in `scripts/tests/deploy.test.sh`) now fails when `deploy/fly/web.fly.toml` has no https `Site__BaseUrl`. A start-time refusal in the web app was **not** added: the web image smoke test (`docker-build-web` in `ci.yml`) starts the image with no `Site__BaseUrl` and the AppHost fake profile (e2e) sets `http://localhost:3000`, so a refusal would break CI, and it could not be run in this session (no way to run the e2e job). If the owner wants it: refuse only when `NODE_ENV=production` and `GABINETY_FAKE` is not `1`, and set `GABINETY_FAKE=1` on those two CI steps in the same PR.
  - Refresh cookie `Path=/api/auth`: **rejected as written.** The proxy refreshes an expired access token itself (`web/src/lib/api/proxy.ts:99` reads `gt_rt` on `/api/proxy/*` requests), so the cookie must reach `/api/proxy`; moving the path would log users out when the access token expires, and a cookie already issued with `Path=/` would need clearing on both paths. Accepted: 7 days, HttpOnly, never in a body.
  - `__Host-` prefix: not done (needs `Secure`, so it cannot be used in the http dev profile); revisit with the start-time refusal.

### F-11 Token validation and edge guard are fail-open on missing configuration (Low)

- **Location:** `Security.cs:26-27` (`RequireHttpsMetadata = false`; audience validated only when `Identity:Audience` is set); `web/src/lib/config/index.ts:23-24` (issuer and audience optional).
- **Scenario:** a service deployed without `Identity__Audience` accepts a token minted by the same authservice for another audience; `RequireHttpsMetadata=false` allows the JWKS to be fetched over HTTP if `Identity:Authority` is ever set to an `http://` address. The staging templates set the audience and an https authority, so there is no exposure with the templates as written.
- **Risk:** Low. **Recommendation:** require audience and an https authority outside the non-deployed profile (fail at start). Proposed task: T-10.16 (same task as F-10).
- **Status (2026-11-18): FIXED for the services in this PR (T-10.16).** `InternalAccess.ValidateIdentity`, run by the same start-time hook as the internal key checks (`AddInternalKeyValidation`, already in all four `Program.cs`): outside the fake profile and outside `ASPNETCORE_ENVIRONMENT=Development` (the `Internal:EnforceKeyStrength` switch applies), the service refuses to start when `Identity:Audience` is empty or `Identity:Authority` is not an absolute https URL. The message names the setting, never a value. Tests in `InternalAccessTests` (empty, blank, http, scheme-less, ftp; fake profile and Development not enforced; forced by setting; removing the call fails 7 tests: tried). The deploy templates are checked by `check-private-topology.sh` (https authority and an audience on all four backends and the web app). `RequireHttpsMetadata = false` stays in the kernel (it is at 400 lines); with the check above it is no longer reachable with an http authority in a deployed profile. **Web side** (`Identity__Issuer` / `Identity__Audience` optional in `web/src/lib/config/index.ts`): same decision as F-10, not changed; the services validate the audience on every call, the edge guard is the weaker layer.

### F-12 Reviews of a suspended or erased practitioner are listed by id (Low)

- **Location:** `ListReviewsFeature.cs:28`: filter is `PractitionerId` and `Published`, not the practitioner's status.
- **Scenario:** anyone with a practitioner id (from an old page or a cache) reads the published reviews of a profile that was suspended or erased. Reviews are anonymous ("Zweryfikowany klient") so the impact is small.
- **Risk:** Low. **Recommendation:** decide and write down whether reviews survive suspension (data classification, guide 8); if not, Reputation needs the status (the push already carries a version).
- **Status (2026-11-18): ACCEPTED pending owner confirmation (supervisor: reviews of a suspended practitioner stay readable until erasure).** Not a cheap fix: Reputation does not know a practitioner's status, so "reviews disappear on suspension" needs a push from Directory (the reputation push already carries a version) or a new internal read, plus a CONTRACTS 5 change. The public listing needs a practitioner id, and the profile page of a suspended or erased practitioner is 404 (Directory returns `Approved` only), so the ids are not published anywhere once the page is gone; reviews are anonymous. Recommendation: **accept for the MVP and write it into the data classification** ("reviews of a suspended practitioner stay readable by id until erasure, and `DELETE /internal/v1/subjects` removes them"; erasure is already implemented in Reputation, `ErasureApiTests`). Decide before launch.

### F-13 Supply chain hygiene (Low)

- **Location:** `.github/workflows/*.yml` (actions pinned by tag: `actions/checkout@v4`, `gitleaks/gitleaks-action@v2`, `pnpm/action-setup@v4`); Dockerfiles (`mcr.microsoft.com/dotnet/aspnet:10.0`, `node:22-alpine`, `postgres:17-alpine` by floating tag; the authservice image is pinned by digest, `ci.yml:119`); `pnpm audit` (dev only): `picomatch` ReDoS (high, GHSA-fc5q-39xc-vjpf), `minimatch` ReDoS (moderate, GHSA-g3hv-75wg-m249) and one more high, all through `eslint-config-next`.
- **Risk:** Low: the audited packages are linters that run on the developer's code in CI, not in the runtime image (**[ran]** `pnpm audit --prod` is clean). **Recommendation:** pin actions by commit SHA and base images by digest with a scheduled update; update `eslint-config-next` when a fix exists. Proposed task: none required (backlog).
- **Status (2026-11-18): ACCEPTED / owner. Nothing changed.** Evidence: **[ran]** `pnpm audit`: the same 3 findings (2 high, 1 moderate), all under `eslint-config-next` (dev only); `pnpm audit --prod`: no known vulnerabilities. A `pnpm.overrides` to force `picomatch` and `minimatch` onto patched versions would put a major version under the ESLint plugins and cannot be verified against `next lint` here; wait for an `eslint-config-next` release. Pinning actions by commit SHA could not be done here: this session can read only this repository, so the SHAs of `actions/*`, `docker/*`, `gitleaks` and `pnpm` cannot be resolved, and `superfly/flyctl-actions@1.5` is in the deploy workflow (`_deploy-app.yml`), which is the owner's. There is no `.github/dependabot.yml`; adding one (github-actions, docker, nuget, npm; weekly; grouped) is the cheap way to get SHA pins and image bumps proposed as PRs, and is left to the owner because it starts creating PRs at once.

## 11. Residual risks (acknowledged, not fixed by this review)

1. Mock payment, mock e-mail and the single provider adapter are by design until T-09.05/T-09.06; F-01 only forces the choice to be explicit.
2. Contact-detail detection is a heuristic (SPEC 6.1 calls it anti-leakage, not security): a user can obfuscate a number.
3. A review's text and the brief go to an external model (the Assistant); the user is told by the product text, the legal text is T-10.02 (owner).
4. Authservice (login brute force, MFA, e-mail confirmation, password hashing, the exchange-code binding, which F-05 works around with our own state cookie) is outside this repository.
5. No MFA for administrators; any `Admin` can approve profiles, resolve disputes and export the ledger (SPEC 4.3 design).
6. The guide's penetration-test caveat applies.

## 12. Not reviewed (needs staging or is out of the repository)

- Headers and TLS actually served by Fly; `force_https`; whether the Fly proxy rewrites `RemoteIpAddress`; whether `/internal/v1/test/*` answers 404 on the deployed apps (needed: CONTRACTS 10.4 integration test); CORS preflight against the public host.
- authservice behaviour: staging value of `Auth__RequireConfirmedEmail`, rate limiting on login and refresh, the exchange-code binding (F-05: read in source at v0.3.4, not run), token lifetimes, JWKS with one key (`scripts/assert-jwks-one-key.sh` exists; not run).
- The e2e journeys and the whole `dotnet test` suite were not run for this docs-only change; tests are cited by reading them.
- The pages' accessibility and SEO rules (not security).

## 13. Tasks to create (High and Medium)

| Proposed id | Title | From | Rank | Notes |
|---|---|---|---|---|
| T-10.08 | Refuse Mock payments outside the non-deployed profile; ownership check on `payments/simulate`; require an explicit provider | F-01 | High | blocks M3; test: deployed profile + Mock fails at start; second user cannot simulate another's booking |
| T-10.09 | Security headers on the web app (CSP with nonce, frame-ancestors, nosniff, referrer, permissions) with a header test | F-02 | Medium | verify on staging after T-09.08 |
| T-10.10 | Private network for Directory, Reputation, Assistant; internal key minimum strength; per-caller keys | F-03 | Medium | depends on the deployment decision about the Booking webhook path (owner) |
| (existing) T-10.03 / PR #110 | Rate limits of CONTRACTS 10.2; visitor address | F-04 | Medium | re-review after merge: middleware order, `RateLimit__TrustedProxyKey` on 5 apps, direct listeners |
| T-10.11 | `state` cookie for the GitHub sign-in callback | F-05 | Medium | done (authservice does not bind the code) |
| T-10.12 | Fallback authorization policy and a route-table test (anonymous list committed) | F-06 | Medium | |

Low findings (F-07 to F-13) are optional backlog: T-10.13 (OpenAPI off outside non-deployed, F-07), T-10.14 (admin separation of duties, F-08), T-10.15 (profile text and URL re-review, F-09), T-10.16 (production config fail-closed, F-10 and F-11).
