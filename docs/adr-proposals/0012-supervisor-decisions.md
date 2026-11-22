# ADR-0012 (proposal): supervisor decisions on four open owner items

Status: **proposed, revisable.** The owner asked for the whole product to be delivered autonomously, so the supervisor decided four open items. The specification
repository is read-only for the implementer; this file records each decision and the CONTRACTS / SPEC text the supervisor should change in `gabinety-tech-spec`.
Code: branch `auto/contract-cleanups`.

## 1. Enum wire casing: the contract wins

**Decision.** Every enum in `Gabinety.Contracts` serialises as CONTRACTS spells it. One converter, `SnakeCaseLowerEnumConverter` (`src/Gabinety.Contracts/Common/WireEnum.cs`:
`JsonStringEnumConverter` with `JsonNamingPolicy.SnakeCaseLower`, integers refused), is put on the enums CONTRACTS spells in lower case or snake_case; it lives in
Contracts, not in the kernel (400/400 lines).

| Enum | Wire | Where CONTRACTS says so |
|---|---|---|
| `Badge` | `email`, `github`, `nip`, `approved` | 3.2 |
| `Confidence` | `low`, `medium`, `high` | 6 |
| `TriageVerdict` | `none`, `hold` | 6 |
| `TriageReason` | `spam`, `abuse`, `personal_data`, `off_topic`, `threat` | 6 |
| `TriageFlag` | `none`, `hold`, `unavailable` | 5.1 |
| `DisputeKind` | `not_as_described`, `practitioner_no_show`, `other` | 4.2 |
| `Resolution` | `release`, `refund_full`, `refund_partial` | 4.3 |

The enums CONTRACTS spells in PascalCase keep the plain string converter and the tests pin that: `PractitionerStatus`, `BookingStatus`, `PaymentStatus`, `LedgerKind`,
`ReviewStatus`, `AssistantState`. The hand-written `Wire(...)` mappers of the Assistant brief and triage endpoints are removed (the DTO now serialises correctly).
Reading is as lenient as System.Text.Json always is (any casing of a name is accepted); writing is exact. The web already consumed the lower-case values (it read the
hand-written mappers' output); nothing changed there. `tests/Gabinety.Contracts.Tests` has the literal wire string and the round trip of every member.

**CONTRACTS to change:** none for these enums; add to section 0 one line: "enums are strings; the spelling of each is in its table; those listed lower-case or snake_case are written that way".

## 2. Review reject reason: 10 to 500

**Decision.** `POST /api/v1/admin/reviews/{id}/reject` takes `reason` of 10 to 500 characters after trimming, like `POST /api/v1/admin/practitioners/{id}/reject` (3.4).
Backend validation (`422 validation_failed`), the web form (`web/src/components/admin/reviews`) and the tests follow.

**CONTRACTS to change:** 5.2 row `POST /api/v1/admin/reviews/{id}/reject`: `{ reason (10 to 500) }`.

## 3. Demo counts: make the data honest

**Decision.** The catalog's review count and average are no longer typed in: `DemoCatalog` derives them from `ReputationPlan` on the Completed bookings of `BookingPlan`,
that is, from what the seeder really creates (three reviews: one each on Anna 5.0, Bartosz 4.0, Ewa 3.0; everyone else 0, including the Suspended demo practitioner, who has no booking).
No booking or review is added, so the seeder's time budget is unchanged (the full fake-profile start was not re-measured: nothing in its work changed; the e2e job measures it).
Consequence: no demo profile shows a rating (it needs 3 reviews), all show "Nowy" until their third review; the public order follows completed count, reliability, availability
and completeness more than rating. Journey 3 starts from Celina, who has no review. `DemoCatalogHonestyTests` fails if a catalog number ever exceeds what the seeder creates.
Not changed: the booking-side numbers of the baseline (`Completed`, cancellations, no-shows, open slots) are still catalog values, and the scale extras (`--factor`) still carry
review counts without review rows, labelled in `docs/operations/PERFORMANCE.md`.

## 4. Suspended or erased practitioner with future bookings

**Decision.** When an administrator suspends a practitioner (`POST /api/v1/admin/practitioners/{id}/suspend`), Directory saves the status and then calls Booking
`POST /internal/v1/practitioners/{id}/cancel-future-bookings`. Booking, through `BookingTransitions` and `RefundCoordinator` (practitioner cancel, CONTRACTS 8.1 and 9.2):
every `Confirmed` booking that has not started becomes `CancelledByPractitioner` with a full refund at any distance from the start, no fee, no payout, `status_reason = practitioner_suspended`,
so the client is not penalised; every `PendingPayment` hold is expired at once (a later payment meets the existing paid-after-expiry full refund); bookings that started or ended are left to
the completion job. New bookings were already refused for a non-Approved practitioner (`422 practitioner_not_bookable`, no slots; the profile is 404). The client is told with the existing
`Cancelled` notification kind and its Polish template, which no transition wrote before: `NotificationSchedulingHook` now writes it to the client, due now, for every `CancelledByPractitioner`
(the practitioner's own cancel included), and removes the unsent reminders and review request. The route is idempotent (`200 { cancelled, holdsExpired, failed }`; `503` when a refund could
not be asked of the provider, the others being done; a repeat finishes the rest; the provider call carries the same idempotency key as any practitioner cancel).
Erasure (`DELETE /internal/v1/subjects/{userId}` on Directory, T-04.10b hooks) makes the same call first and erases only after it succeeded (`503`, nothing erased, repeat). If Directory
cannot reach Booking on a suspension, the profile stays suspended and the answer is `503` telling the administrator to repeat the cancellation (RUNBOOK 9.2); `suspend` itself is not repeatable
(`409`).

**New wiring.** Directory holds a key for Booking: GitHub secret `INTERNAL_KEY_DIRECTORY_TO_BOOKING` = `Internal__Keys__Booking` on Directory = `Internal__Callers__Directory` on Booking
(`deploy/secrets.map`, `deploy/fly/*.toml`, DEPLOY.md); Directory gets `Backend__Booking`. Route row in `docs/operations/ROUTE-AUTH.md`; the callers matrix of SECURITY-REVIEW section 4 has the new pair.

**Supervisor decision (deviation from CONTRACTS 8.1).** A cancellation caused by the cascade (`status_reason = practitioner_suspended`) is **not** counted in `practitioner_cancel_count`; a voluntary practitioner cancel still is. `StatsCalculator` does this, tested in `StatsPushCalculationTests`. Caveat: erasing the practitioner clears `status_reason` of their bookings (Booking erasure), after which those rows would count; an erased practitioner has no ranking, so it does not matter. CONTRACTS 8.1 should say "`practitioner_cancel_count` + 1, except for a cancel made by suspension".

**Open points.** (b) accepted by the supervisor: a `Cancelled` e-mail now also goes to the client when a
practitioner cancels by themselves (it was only a template before); CONTRACTS 8.1 should list it. (c) A booking that has started but not ended when the suspension happens is not cancelled.

**CONTRACTS / SPEC to change.** CONTRACTS 3.4 `suspend`: "then Booking cancels the practitioner's future bookings with a full refund (4.3)"; 3.6 `DELETE /internal/v1/subjects/{userId}`: "first the same cancellation";
4.3 internal table: `POST /internal/v1/practitioners/{id}/cancel-future-bookings`, caller Directory, `200 { cancelled, holdsExpired, failed }`, `503` when incomplete; 8.1: a `Confirmed` cancel by the platform
for a suspension, reason `practitioner_suspended`, notification `Cancelled` to the client; 10.3: `Internal:Keys:Booking` on Directory and `Backend:Booking`. SPEC 5.1: Directory -> Booking (30 s, once, idempotent).

## 5. authservice rate limit: count visitors, not the web app (2026-11-22)

**Context.** authservice limits its auth endpoints to 20 requests a minute per client address (hard-coded, `Program.cs`). The web app calls it server-side, so every visitor arrives from the web app's address and all of them would share one bucket (found by the real-authservice tests, PR #124).

**Options read in the source.** (a) `Network__ClientIpHeader` (a trusted single-valued header carrying the client address), plus `KnownProxies`/`KnownNetworks`/`TrustAllProxies` for `X-Forwarded-*`: exists. (b) A configurable limit: does not exist. So (a).

**Decision.** `Network__ClientIpHeader=Fly-Client-IP` on authservice (fly toml, AppHost). Fly's edge sets that header for public traffic; the web sends the visitor's validated address in it on its server-to-authservice calls only. `X-Forwarded-For` was not used: it is multi-valued and client-appendable, and the header option needs no proxy lists. The Blazor gateway must forward the address the same way.

**Consequences.** Visitors have separate buckets (tested against the real image). To verify on the first deploy: Fly's edge overwrites a client-sent `Fly-Client-IP`. Not changed: a `429` on refresh still ends the session in the web. Proof and details: `docs/operations/AUTH.md` section 9, finding 4.

## Ops decisions of the Blazor port (branch `port/ops`: e2e on both hosts, deploy, capacity, cutover)

Decided by the `port/ops` session on the supervisor's behalf; revisable.

1. **Both hosts are required checks until the cutover.** CI `e2e` is a matrix on `host: [next, blazor]`, each leg on its own fresh fake profile and in parallel (the journeys write data and J3 cannot run twice on one stack), so wall time is that of one run. The Next leg keeps the job name `e2e` (the required check by that name stays valid); the Blazor leg is `e2e-blazor`. Two jobs would have cost the same time and duplicated 35 lines.
2. **The Blazor journeys skip, they do not guess.** A journey whose page the Blazor host does not serve (the probe is a live anonymous GET: 404 means not ported) is skipped with the owning port branch in the reason; `PARITY_STRICT=1` makes it a failure, which is what the cutover gate runs. Both hosts run every journey (`playwright.config.ts` no longer lists the specs the Blazor host supports); J7 has a Next variant (via the BFF) and a Blazor variant (`*.blazor.spec.ts`, `port/auth`), each ignored on the other host.
3. **No BFF on Blazor means set-up goes to the backends.** On `HOST=blazor` the journeys' API set-up and verification use the user's bearer token directly against the service (the token comes from DevIdentity); the browser context is signed in through the real form. This is a test-harness decision only; the product's browser never holds a token.
4. **Parity gate** is `scripts/check-parity.mjs` (not a dotnet test): it reads `web/src/app` and `src/Gabinety.Web` as text, report-only by default, `CUTOVER_GATE=1` (the repository variable) makes a missing route fail. The `/api/*` BFF routes are exempt with a written reason (`scripts/parity-exemptions.json`). The text scan is a heuristic (`@page`, `MapGet`/`MapPost` literals, `...Path` constants); a route registered another way needs an exemption or a constant, and the final authority is the journeys under `PARITY_STRICT=1`.
5. **The Blazor app is a new Fly app, `gabinety-web-blazor-staging`, beside the Next one.** The old app is the rollback and no deploy mutates the live site in place; the public host's certificate moves in a person's step (`CUTOVER.md` section 3). The cost is one more always-on machine for a week or two (about 3.3 USD a month while both exist).
6. **The cutover is gated by a commented estate row**, not a flag: `#cutover# web-blazor ...` in `deploy/estate.conf` is invisible to every script until uncommented; `deploy.yml` already has the `deploy-web-blazor` job (gated on the row's output) and `_deploy-app.yml` knows the volume. `deploy.test.sh` checks the row as if enabled, so the cutover PR flips one switch.
7. **Single machine, no scale to zero, a 1 GB volume for the Data Protection keys**, as the ADR says; plus the proxy's `connections` limits (soft 100, hard 150) as the load-shedding valve below the 512 MB budget. The volume is root-owned and the image is non-root: the workflow runs `chown -R app /data` after each deploy (idempotent) instead of changing the image to start as root (the image check asserts non-root).
8. **Capacity is reported as a planning figure with its bounds**, measured in a CI-like container with a Release build and a GC heap cap emulating 512 MB, never as a Fly result (`PERFORMANCE.md` "Blazor circuits").

## 6. Blazor web: stateless sealed session, header, registration (wave 2, port/auth, 2026-11-22)

**Session (ADR-0010 cutover precondition, done).** `gt_session` is now the cookie handler's ticket sealed with Data Protection (`DataProtection__KeysPath`, a persisted shared key ring; the AppHost
gives the fake profile a per-user temp path, Fly gets a volume). It holds the claims, a random session id and the identity service's **refresh token** (an authentication token of the ticket, not
a claim); never the access token (about 1.5 KB sealed, under a 3.5 KB budget test). The access token is minted on demand from the refresh token and cached per session id (`SessionCache`, a
process-wide cache, so a circuit and a request share it). Because authservice rotates the refresh token on every use and a replay revokes the family, and a circuit cannot write a cookie, whoever
rotates leaves the newest refresh token in the cache and the **next response that can** writes it into the cookie (`OnValidatePrincipal` registers the catch-up on `OnStarting`). One rotation per
session at a time (a keyed lock), the new refresh token is stored before anything can fail, a 429/5xx/outage keeps the session (the identity service is not asked again for 5 s), a refused refresh or
a logout ends it (the dead cookie is deleted; logout revokes at authservice, which revokes every refresh token of the user). **Known limit:** a restart empties the cache; if a circuit had rotated
the token and no response carried it into the cookie before the restart, the cookie's token is a spent one and the user signs in again (authservice treats it as a replay). Not solvable without
server-side state; rare (an interactive page open over an hour across a deploy with no page load in between). Tests: tampered cookie, other key ring, key added to the ring, revoked ring, deploy,
concurrency (16 callers, one rotation), mutation-checked.

**Header.** Order of the main nav is `Programiści` first, other public pages append to `SiteNavigation.Primary`. A signed-in user sees `Moje rezerwacje` and `Panel programisty` (decision: any signed
in user, because a practitioner is a profile in Directory, not a role of the identity service, and the panel is also where a client becomes one; the shell makes no extra backend call), an
administrator also `Administracja` (role claim as a string or an array), the account menu holds `Konto` and `Wyloguj się` always. `Zostań programistą` (audit open question 1) is the panel link.
Footer links `/regulamin` and `/polityka-prywatnosci` lead to pages the static-pages session ports.

**Sign-in states.** `429` is its own text (wait), a lockout (`401 {lockedOut}`) its own text without a time, a wrong password and an unknown address stay one text. The 2FA second step is a real
page, `/logowanie/kod`: the challenge token is sealed into a 5 minute `gt_2fa` cookie (HttpOnly, Lax, path `/logowanie`), never in a URL, a form or the page; authenticator codes lose spaces and
hyphens, recovery codes only spaces (the hyphen is part of the code). A `202` registration goes to `/rejestracja/sprawdz-skrzynke`. Password rules are ASP.NET Identity's (ASCII letters, a
Polish letter is not a lower- or upper-case letter for it). The consent versions come from authservice and travel in hidden fields; they are never printed (a `title` on the links).

## Wave 2, public pages of the Blazor host (branch `port/public-pages`, 2026-11-22)

**Decisions** (supervisor session; revisable). (1) **Slot picker without JavaScript**: the profile renders each offer's free times in the first response (one Booking read per 14-day window, anonymous). A strip of days with free times, the times of ONE day, "Pokaż kolejne dni" are plain links whose state is in the URL (`?okna=`, `?oferta=&dzien=`), so the page works with scripts off and is cached by route and query. A time links to `/rezerwacja/{offering}?start=<utc>&gabinet=<slug>` exactly as the Next.js page does (the task text said `slot=`; the Next.js page and the journeys use `start`). (2) **Reviews** are the first page rendered in the HTML with a "more" link (`?opinie=<cursor>`), not a client fetch. (3) **F-05**: the Directory pages by cursor and has no total, so a filtered or paged variant states a count only when its page is the whole result, and otherwise none; the unfiltered category keeps the sitemap's count (journey J1 reads `Wyniki: N`). (4) **404 of a profile** is the router's not-found page, which says "Nie znaleziono programisty" for `/gabinet/...` (the status-code re-execution does not let a page that sets 404 itself render its own body); a `410` from the Directory is a 410 with the same copy. (5) **Output cache**: `OutputCache:Enabled` (default true; the AppHost's Blazor host sets it false for the journeys) and `OutputCache:Seconds` (default 300). The policy stores only a plain `200` without `Set-Cookie`, and refuses a request with the `gt_session` cookie or an `Authorization` header; a cached page is therefore never personal. (6) **Legal text** is copied to `src/Gabinety.Web/Content/Legal` (embedded); a test pins both copies identical until the cutover moves them. (7) **Contents list** on legal pages lists the file's `##` sections. (8) **Polish text** lives in `Text/*.cs` (the source-rule test now allows the whole folder).

**Not decided here.** Sort control of the list (the Directory has no sort parameter), the phone-collapsed `Filtry (n)` (a `<details>` is open by default because collapsing at a breakpoint needs script), the price range in the home trust row (needs a Directory read of its own).
