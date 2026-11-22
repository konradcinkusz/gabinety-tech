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

## 6. Booking flow and client area in the Blazor host (branch `port/booking`, 2026-11-22)

**Scope.** `/rezerwacja/{offeringId}`, `/checkout/mock/{bookingId}`, `/rezerwacja/potwierdzenie/{bookingId}`, `/bookings/{bookingId}` (new, see c), `/moje/rezerwacje`, `/moje/rezerwacje/{id}`, `/moje/opinie`, `/moje/opinie/{bookingId}`.
Decisions taken by the implementer, all revisable:

a. **Plain form posts with antiforgery, no circuit, except one clock.** Every action of these pages (ask the assistant, apply its proposal, pay, simulate a payment, cancel, dispute, report a no-show, review, reply) is a
   form post handled by the page; the role of `/moje/rezerwacje` is a tab (a link), the status filter a GET form, "more" a link with the cursor; a cancel or dispute opens by a GET form's button (`?action=`) and a result is a
   redirect carrying a fixed key (`?done=`, `?sent=`, `?replied=`), so a reload never repeats a write and nothing from a query string is rendered. Reason: DESIGN-CONTRACT 9.7 (an input typed before a circuit connects is lost)
   and ADR-0010 (a circuit only where needed). The one interactive piece is `HoldTimer` (`@rendermode` on that component in `MockCheckout.razor`): the checkout's countdown. `MyBookings` is no longer interactive: the foundation's
   tests that pinned it are rewritten. **Cost:** the character counters count what the server received (after a post), not live; the assistant needs a post and a page load, not a fetch.
b. **The create answer's field is `checkoutUrl`** (CONTRACTS 4.2, `CreateBookingResponse`), not `paymentUrl`. The page follows it generically (`CheckoutTarget`): a path of this site (the mock checkout) or an absolute `https`
   URL (Stripe Checkout); `//host`, `http` on another host, `javascript:`, user-info and control characters are refused. `practitioner_payments_not_ready` (422) has its own Polish message.
c. **Stripe's return page.** The Booking service gives Stripe Checkout `{Site__BaseUrl}/bookings/{id}` (`CheckoutRequest.SuccessUrl`, `docs/operations/PAYMENTS-GO-LIVE.md` step 4). That route exists in neither frontend, so the
   Blazor host answers it with a redirect to the confirmation, which says "Czekamy na potwierdzenie płatności." and reloads itself every 5 s (meta refresh) while the hold lasts. **Owner checklist (service side, not configuration):**
   `CreateBookingFeature` builds the URL as `{Site__BaseUrl}/bookings/{id}` in code (also used as Stripe's `cancel_url`); pointing it at `/rezerwacja/potwierdzenie/{id}` is a one-line change in the Booking service, after which the extra route can go.
d. **The mock checkout gate is copied, not asked.** No backend endpoint says "the mock provider is on". `WebOptions.MockPayments` applies the rule the Booking service uses for its `simulate` route (`GABINETY_FAKE=1` or
   `Environment:Kind=Development`) and the provider rule of the Next.js app (`Payments:Provider` unset or `Mock`): anywhere else the page is a 404 before any backend is asked, and the service refuses the simulation anyway (403).
   If the service ever exposes its provider (for example on `GET /api/v1/payments/provider`), switch to it. The web app has no `Payments__Provider` in `deploy/fly/web.fly.toml`, on purpose (F-01): not changed.
e. **`cancelOutcome` (F-11), consumed since #130.** `BookingDetailDto.CancelOutcome` is `{ outcome: full_refund|partial|no_refund, refund: Money }`, present only while `canCancel`. The dialog says the amount the
   service computed ("Otrzymasz zwrot 180,00 zł.") **and keeps the general rule below it**, so journey J5 (which reads "co najmniej 24 godziny, zwrot wynosi 100% ceny" and "mniej niż 24 godziny, brak zwrotu" in the dialog)
   passes either way; without the field only the general rule shows. The page never computes it. Tests: `BookingFlowModelTests.With_a_cancel_outcome_*`, `BookingDetailTests.*outcome*`, `ContractCopiesTests`.
f. **Party names (F-03).** The pages read `practitioner.displayName` and `client.displayName` of the booking and leave the row out when it is empty: no "Programista: Programista", no leading separator
   (`BookingFormat.MetaLine`). Nothing to change when the backend starts filling them.
g. **Per-request budgets (SPEC 5.1).** All clients share one resilience configuration (the kernel registers one handler with one options name), so a per-client timeout is not possible. The options now carry a
   `TimeoutGenerator` that reads `RequestContext.Timeout` from the request: the booking write asks for 12 s (the payment provider call is 10 s) and the assistant brief for 35 s; everything else keeps 5 s, and a write is never retried.
   `ClientAreaClientTests.Through_the_real_pipeline_*` proves it against the real handler chain (5.5 s answer: the write with a budget succeeds, the one without is cut).
h. **Authenticated pages answer 200 with a `role="alert"` block** when a backend is down (as the foundation's `MyBookings` did), not 503: they are `noindex`, and a status adds nothing for a signed-in user.
i. **The cancel confirmation is a `<dialog open role="alertdialog">` in the markup, not a modal.** A modal needs `showModal()` (script). It has the title as its accessible name, the safe button ("Nie anuluj") beside the
   danger one, and `autofocus` on its title; it has **no focus trap and no Esc** (DESIGN-CONTRACT 5.11 and 7.10 are not met until a script is allowed). Same for the no-show dialog.
j. **Status filter** lists the ten states and "Wszystkie", not only those the user has (the list is paged, so the page cannot know them): P-BKS-1 stays open on that point.
k. **`app.css`** gets one additive block of the shared classes these pages use (`.form`, `.field__*`, `.btn--secondary|ghost|danger|ghost-danger`, `.badge--info|warning|danger|neutral`, `.dl`, `.tabs`, `.dialog`, `.sr-only`),
   written with the foundation's token names. The contract's full sheet replaces the block when it lands.

## 6. UI-audit backend defects: party names, cancel outcome, dispute window, auth stand-in (2026-11-22)

Branch `auto/audit-backend`; source: `docs/ui/UI-AUDIT.md` section 6 (F-02, F-03, F-11, F-13). The frontend items are fixed in the Blazor port.

**F-02, DevIdentity.** On current main `GET /api/v1/auth/consents` already exists in the stand-in and is covered by the shared `AuthContractSuite` (it ran in the unit tier against DevIdentity and in the integration tier against the real image); the audit was taken against an older build. What was missing: the routes the web calls and the contract did not pin. Added to the stand-in `GET external-auth/login` (`400`, provider not configured) and `POST external-auth/exchange` (`400` without a code, `401` for any code: none is ever issued), and to the suite (so the real image must agree): `Accepting_the_consents_answers_the_status_and_needs_a_token`, `Consents_have_the_cookie_entry_the_account_page_reads`, `Exchanging_a_code_that_was_never_issued_is_401`. `DevIdentityRouteSetTests` fails if any route listed in `web/src/lib/session/identity.ts` (both the `/api/v1` and the `/api` alias) answers `404`/`405` in the stand-in. Not asserted against the real image: `external-auth/login` (it redirects when GitHub is configured).

**F-03, party names on booking reads.** `BookingView.practitioner.displayName`/`slug` and `client.displayName` are filled on every read that builds a view (detail, both lists, cancel, dispute, no-show report, admin resolve).
- Practitioner: new internal call `GET /internal/v1/practitioners/{id}` on Directory (`{ practitionerId, displayName, slug }`, `404` unknown, key `Internal__Keys__Directory`, no new secret) through the existing typed `IDirectoryClient` (3 s, once). One call per distinct practitioner on a page. If Directory cannot answer, the view still renders with the label `Programista` and an empty slug (a name is not worth a failed page). Erased practitioner: Directory already holds `Usunięty użytkownik`; Booking also short-circuits when `practitioner_user_id` is an erasure token.
- Client: there was no snapshot, and the token's `name` claim is the e-mail in authservice. New column `bookings.client_display_name` (migration `T-12.01_ClientDisplayName`), written at creation as `Klient` plus the upper-cased first letter or digit of the `name` claim (`Klient D.`); no claim: `Klient`. No e-mail, no surname, one character of the claim. Rows made before the column read `Klient`. Erasure of the client nulls it and the view then reads `Usunięty użytkownik`. The existing CONTRACTS 4.4 rule stays: the practitioner sees the client only from Confirmed onward.
- Web: `web/src/components/bookings/api.ts` types and comments updated; the components already printed `practitioner.displayName`, so the empty-separator symptoms disappear without a component change. The Playwright journeys do not assert these strings; no journey change.

**F-11, `cancelOutcome`.** `BookingView.cancelOutcome` = `{ outcome: full_refund | partial | no_refund, refund: Money }`, present only when `canCancel` (for the viewer's role), else `null`. It is computed by `CancelPlanner.Plan`, the function `CancelFeature` now calls to decide the refund it asks the provider for, so the dialog and the money cannot differ. Tests: at exactly 24 h before the start `full_refund` 10000, 1 s later `no_refund` 0, each compared with what the cancel then really does (payment status and refunded amount); the practitioner is always `full_refund`; a booking that cannot be cancelled has `null`. `partial` is in the vocabulary only; no cancel rule produces it today.

**F-13, dispute button.** The state of main already allows it: `Completed -> Disputed` until start + 72 h (#127), and the seeder completes its three Completed bookings with the clock moved forward and then back, so with the clock at its baseline each is `Completed` with its start still ahead and `canDispute` is true. Deviation from the task text: **no fourth Completed booking was seeded.** `DemoCatalog`'s counts are derived from exactly the Completed bookings with a review (decision 3); a Completed booking without a review would make them dishonest again, and the existing ones already sit inside the window. Instead: `DisputeWindowTests` pins `canDispute` at start-48 h, start, start+72 h-1 s (true) and start+72 h (false), client only; `BookingSeedStepTests.Demo_client_has_a_Completed_booking_inside_its_dispute_window_and_the_API_says_so` fails if the seed ever stops giving the demo client one. The fake-profile run time was not measured here (no Docker in this session); the seeder's work is unchanged, so its time is too. If the audit still finds no button on a fresh stack, the cause is in the web, not Booking.

**CONTRACTS / SPEC to change.** 3.6: add `GET /internal/v1/practitioners/{id}` (caller Booking) returning `{ practitionerId, displayName, slug }`; 4.1: `client_display_name` (a snapshot, "Klient X."); 4.4: `BookingView` gets `cancelOutcome?` as above, `practitioner.displayName` is Directory's name or `Programista`, `client.displayName` is the snapshot or `Usunięty użytkownik`; 9.2: the 24 h rule is evaluated by one function for the preview and the cancel.

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


## Wave 2, admin area of the Blazor host (branch `port/admin`, supervisor session of the admin port)

Decisions taken while porting `web/src/app/admin/**` to `src/Gabinety.Web/Components/Pages/Admin`. Revisable; nothing here changes a contract.

**F-10: the forbidden page is a real 403.** `/admin/*` pages carry `[Authorize(Policy = "Admin")]` through `Pages/Admin/_Imports.razor` (so a page of the folder cannot forget it; the route audit asserts every `/admin*` route has policy `Admin`).
The policy is `RequireAuthenticatedUser` + `ClaimMapping.IsAdmin` (role `Admin` or `SuperAdmin`, string or array claim). An anonymous visitor gets the cookie challenge: `302 /logowanie?redirect=<path and query>`
(unchanged). A signed-in non-admin gets **403** with the "Brak dostępu" page (`data-testid="forbidden"`, `role="alert"`, link home): the cookie's access-denied event now sets 403 instead of redirecting to `/`,
and the status-code middleware re-executes `/not-found?status=403`, which renders `ForbiddenNotice` for 403 and the "not found" page for everything else. A *post* of a non-admin gets a plain `403` text body (a re-executed
POST would fail antiforgery and answer 400). No backend is called for a refused user (tests). A service that answers 403 although the cookie says admin (a role withdrawn since sign-in) is shown the same way (status 403).
The services remain the boundary.

**Static server rendering, no circuit, for the whole area.** Every state change is a static form POST handled by the page (`@formname`, `<AntiforgeryToken />`), validated again on the server at every step, followed by a
redirect with a fixed flash key (`?flash=admin-approved`): no text of the query is ever rendered. Forms are named `verb-<guid>`; a post for a row that vanished between the page and the click (somebody else decided first) would
make Blazor refuse the post for a form it did not render, so `StaleFormGuard` renders an empty form with the posted name and the page answers "Ten wpis już się zmienił" with a fresh list instead of an error page (tested).
`Odrzuć`, `Zawieś`, `Rozstrzygnij` only *open* the next step: they are GET forms (a button, so the journeys' hook `button "Rozstrzygnij"` holds; nothing changes on the server).

**Confirmation steps.** Approve and reinstate: one click (J2 clicks once). Reject (practitioner, review) and suspend: a step with the reason (10 to 500 after trimming, `TextRule`, #125 and ADR-0012 decision 2) and the consequence in
words. Resolve dispute: choose, then "Przejdź do potwierdzenia" (validates, shows what the money does, including the rest the practitioner keeps), then "Potwierdź rozstrzygnięcie"; "Wróć" keeps what was typed. 409 from a service is a
clear message and a re-read list, 503 on a resolve keeps the confirm step open for a retry, 503 on a suspend says the profile IS suspended but future bookings are not all cancelled yet (RUNBOOK 9.2). A POST is never retried
(SPEC 5 rule 8; tested through the real handler chain).

**Disputes (P-ADM-3).** The list item carries no price or names, so the page reads each booking (`GET /api/v1/bookings/{id}`, an administrator may; at most 5 reads at once, per page of 20) and shows offer, price, payment state and
"Najwyższy możliwy zwrot" (also the hint of the amount field, whose upper bound is checked before the post). The practitioner is named through Directory's admin read. **Limit found:** Booking still returns an empty practitioner name and
no client name (`BookingViewFeature` says so: F-03 / CONTRACTS 3.6 open question), and no service gives an administrator a client's name: the client is shown as a short reference (`Klient demo-cli`, the id in a `title`).
The booking id is on the card as secondary text because journey J6 addresses the card by it. The amount field is revealed by CSS only (`:has(:checked)` under `@supports`), so there is no script; without `:has()` it stays visible.
**No separate dispute detail route:** J6 resolves from the list card, the card holds every fact, and the Next.js app had no such page either; add one only if a requirement appears.

**Verification queue (P-ADM-2).** The journeys address a row as a `listitem` with the practitioner's name as a link (`VerificationQueuePage.row`), while the design contract says "table". The hook wins (DESIGN-CONTRACT rule 5):
rows are `ul > li` cards that read like table rows (name, status badge, checks, dates, then actions with a gap). The ledger and the suspicious pairs are real tables (`th scope="col"`, `.table-wrap` region, labelled cells under 40 rem).

**Ledger (P-ADM-4) and the download.** The page shows the period (presets, two dates, primary "Pokaż okres"), the summary, "Pobierz CSV" and the first 50 entries as a table read from the same CSV (the services have no JSON ledger;
the preview stops reading after 51 rows). `GET /admin/ksiega/csv?from&to` (policy Admin) **streams** Booking's `ledger.csv` with the administrator's own bearer: the period is validated here with the services' rules (400 without a
backend call), the first buffer is read BEFORE the response starts (a backend that fails at once is a `503` problem the browser can show, never an attachment), then the body is copied as it arrives (a test proves the first rows
reach the browser while the backend still waits). `Content-Disposition` is built here from the validated days (the same name J6 asserts). **An error after the response started aborts the connection** (`HttpContext.Abort()`), so the
browser reports a failed download instead of saving a file that looks complete; the exception text, which can carry the backend address, is not logged. The browser leaving cancels the backend call (tested). Only the headers wait
for the 5 s of the read profile; the body is not time-limited here.

**Shell.** One `.tabs` strip under the header (`AdminLayout`, `aria-current="page"`, distinct `aria-label`), `Administracja` in the header for administrators only (`SiteNavigation.Items(signedIn, isAdmin)`), status switches as
joined links (`SegmentedLinks`), badges for every status family with the label always present (G-8). Dates are `24 lis 2026, 13:00` in Europe/Warsaw; ids are never the primary text.
**Tokens:** the admin stylesheet uses the foundation's token names (`--color-*`, `--text-*`), the ones that exist today; the design contract's names differ (`--surface`, `--fs-*`), so the session that swaps `tokens.css`
must map `AdminLayout.razor.css` too (one file). Polish text is in `Text/Admin/PlAdmin.cs`; `SourceRulesTests` now allows Polish under `Text/` (it allowed only `Text/Pl.cs`).

## Practitioner area (wave 2, branch `port/practitioner`)

Decisions made by the session that ported `/gabinet-programisty/*` to the Blazor host. Revisable; the code points here.

**Any signed-in account may become a practitioner (F-10, audit open question 1).** The dashboard of an account with no profile says so on the page ("Każde zalogowane konto może założyć gabinet programisty. Nie musisz zakładać
osobnego konta, a konto klienta zostaje bez zmian.", hook `data-testid="open-to-all"`). The header shows one entry, `Panel programisty`, to every signed-in account (`SiteNavigation.PractitionerPanel`): whether an
account already has a profile is the Directory's knowledge (`GET /api/v1/me`), not a claim of the token, and a Directory call on every page render for the sake of one label is not worth it, so the shell does not
guess and the dashboard onboards. The practitioner guard is per page: no session is a redirect to sign-in (the cookie scheme), an account with no profile is a redirect from every sub-page to the dashboard, a
service that is down is a `503` page with a retry link (not a `200` error page).

**Re-enabling an offer needs a service route (F-04, P-OFF-1): `POST /api/v1/me/practitioner/offerings/{id}/activate`.** The Directory had `deactivate` and said "there is no reactivation"; the audit asks for a way back.
The route is the mirror of `deactivate`: `204`, idempotent, the practitioner's own offer only (`404` otherwise), `422 too_many_offerings` when five are already active (the same limit as create). It is the only change
outside `src/Gabinety.Web`; tests in `OfferingApiTests`, row in `ROUTE-AUTH.md`. **CONTRACTS to change:** 3.5 offerings table: `POST /api/v1/me/practitioner/offerings/{id}/activate` -> `204`; `404`; `422 too_many_offerings`.
Disabling needs a confirmation step on the page (`?wylacz=<id>`, `role="alertdialog"`, no JavaScript), enabling does not (it is the undo).

**The Stripe return address.** Booking (PR #127) hands Stripe `{Site__BaseUrl}/practitioner/payments?return=1` and `?refresh=1`; those are fixed in the service. The page of this host is `/gabinet-programisty/platnosci`
(the area's own address); `/practitioner/payments` is a signed-in route that forwards to it keeping only the two flags it knows. Changing the service to use the new address is the cleaner end state and is not needed.
The onboarding link is shown on the page after the post and followed by the practitioner, not answered with a redirect: the CSP says `form-action 'self'`, which browsers also apply to a redirect that answers a
form post. A link that is not an absolute `https` URL without credentials is never shown. Nothing about the link is stored or logged.

**The payments banner and the demo provider.** The dashboard banner ("Płatności nie są jeszcze gotowe") shows when `GET /api/v1/me/payments/account` answers `none` or `pending`. The Mock provider answers `404`: that is
"nothing to set up" (the fake profile hides the banner and the payments step reads "tryb demonstracyjny"). An answer that cannot be read (Booking down) shows no banner and is never offered as the next step.

**Profile label.** The bio field stays labelled with the word `Opis` ("Opis (o mnie)"): journey J2 finds it with `getByLabel("Opis")` and the Next.js host runs the same journeys until the cutover. When the cutover
removes `web/`, the label can become `O mnie` with the page object changed in the same PR (DESIGN-CONTRACT 9.5).

**Static pages, no circuit.** The whole area is static server rendering with plain forms (antiforgery, post-redirect-get with a flash by fixed key, errors in a summary and under each field). No page in the area runs a
circuit, so the journeys have no connection to wait for and the pages work without JavaScript. The availability editor keeps its draft in the form (`rule-N-day`, ...), add and remove are posts that show the draft again.

## 6. J3 flake: the review-to-Directory push waited for a 60 s timer (branch `fix/j3-flake`, 2026-11-22)

**Symptom.** J3 failed about one run in three with `the third review reaches the Directory  Expected: 3  Received: 0` (PRs #124 and #132; they passed on rerun).

**Root cause.** Reputation pushes a rating summary to the Directory only from `SummaryPushHostedService`, a `PeriodicTimer` of 60 s on the real timer (`OffsetTimeProvider` hands timers to the system provider, so the harness's clock jumps do not move it). A review committed just after a tick therefore reaches the Directory up to a whole interval later, and J3 waited exactly 60 s (`expect.poll` timeout 60 000 ms): the wait budget equalled the period, with no room for the push call itself. It was one in three, not rare, because the suite's timing is nearly the same every run (the AppHost starts, the seeder finishes, J1 and J2 run, J3 books and completes), so the third review kept landing within seconds of the same point of the timer's grid. In the failed job the poll window opened at about 10:48:36 and the timer's grid, set by Reputation's start at about 10:44:36, has a tick at :36 of each minute.
Ruled out by reading the code: the version ordering (Reputation's version is a per-practitioner counter, the seeder's baseline 1 only swallows the first review's push, and the third review's version is higher than anything stored), the `StatsPushJob` (it pushes booking stats, not the rating, and its timestamp version is a separate column), the seeder (it pushes version 1 once, before the tests), the clock offset (not read by the job's timer), a lease or lock (none; the job has an in-process gate only), and the review being created before the booking is Completed (the create call checks Booking's reviewability and answered Published). The CI log carries no service output, so the grid argument is an inference from timestamps and the code, not an observation of the job; the fix removes the dependency on the grid, which is what makes the test deterministic.

**Decision.** Fix the product, not the test: a published review now signals the push loop (`SummaryPushSignal`, a coalescing one-slot channel), which runs a cycle at once; the 60 s timer stays as the safety net (an unpushed summary, a Directory outage, a restart). The test's budget was not raised and nothing retries or sleeps. The lag a reviewer's rating takes to appear on the profile drops from 0 to 60 s to the length of one HTTP call. Only the create path signals; DemoReset and the reply path change no rating the Directory shows, and the timer still carries any other change.

**Proof.** `SummaryPushPromptnessTests`: a published review is pushed with no tick (the host's clock is manual and the real timer is 60 s); fails when the `Notify()` call is removed (mutation run, the push list stays empty); a Held review pushes nothing.

## 7. Test hygiene: shared InMemory store in the triage rig, hard-coded migration lists (branch `fix/test-hygiene`, 2026-11-22)

**Flake.** `TriageTests.Policy_refusal_...` failed once with `Assert.Single got 2 usage rows`. Root cause: every Assistant host in one process registers its InMemory database under the same name (`assistantdb`); `TriageRig` papered over that with a random UTC day in 2100..2154, so two rigs drawing the same one of ~20 000 days (here 2113-01-30) saw each other's rows (birthday collision across the suite's rigs). Fix: each rig replaces the context registration with a private `triage-<guid>` store and a fixed clock; `Rows()` reads that store only. Test `Rigs_running_side_by_side_at_the_same_instant_each_see_only_their_own_usage_rows` runs two rigs at the same instant and fails (expected 3, actual 4) when the private name is put back to the shared one (mutation run). No retries, no delays, no static state added; the product registration is unchanged.

**Migration lists.** Directory, Reputation and Assistant skeleton migration tests now use the Booking model: `Expected` = the assembly's `Database.GetMigrations()` (via the design-time factory), `Shipped` = the pinned released prefix; the applied history must equal `Expected` and start with `Shipped`. A new migration no longer edits these tests. Failure power checked offline against `AssertHistory` for all three (Expected equals Shipped today): a dropped, an extra, a renamed released and a reordered migration each fail. The Docker-backed run is the CI integration job.

## 8. The cutover PR: retire Next.js (branch `cutover/retire-nextjs`, 2026-11-22)

The owner wants the product delivered autonomously; the supervisor decides the open points (revisable). `docs/operations/CUTOVER.md` sections 0 and 1 were executed as one PR; sections 2 and 3 stay the owner's go-live runbook.

- **`deploy/fly/web.fly.toml` is deleted in this PR, not after the old app is gone.** It named `web/Dockerfile`, which no longer exists, and `scripts/check-fly-toml.sh` (a required check) rejects a config whose Dockerfile is missing. No `flyctl` command that keeps, scales or destroys the old app needs the file. Redeploying the old app needs the commit before the cutover (CUTOVER.md section 4). The `web` row of `deploy/secrets.map` goes with it (nothing deploys that app any more; the secret already staged on it stays).
- **The estate key stays `web-blazor`** (Fly app `gabinety-web-blazor-staging`, a NEW app beside the old one; the old one is the rollback). Only the CI check name is `web` (`docker-build-backend (web)`), as CUTOVER.md says. Renaming the AppHost resource and the estate key to `web` would also rename the Fly config, the job and the secrets rows: not worth the churn, revisit with the retirement of the old app.
- **`PUBLIC_URL_WEB` becomes `PUBLIC_URL_WEB_BLAZOR`.** The old variable (if the owner set it) names the old public host; reusing it for the new app's smoke would probe the wrong app.
- **The Vitest test of the Next handlers against the real authservice left CI with `web/`.** The Blazor host's identity client has no real-authservice test yet (follow-up, listed in the PR and in `AUTH.md`): fakes cover it, the journeys cover it end to end against the stand-in.
- **Nothing was run against Fly, DNS or the owner's accounts.** The repository variable `CUTOVER_GATE` is the owner's to delete (an agent cannot).
- **Precondition 7 (memory)** was restated from `PERFORMANCE.md` section 8, not re-measured: the host code did not change in this PR. Re-run `ONLY_CIRCUITS=1 scripts/measure-performance.sh` before the go-live if it changed since.

## 9. Production configuration: e-mail, model, config coverage (branch `fix/prod-wiring`, 2026-11-22)

**Decision: e-mail is `Smtp` in the deployed estate only when it can work.** `deploy/secrets.map` stages `Notifications__Provider=Smtp` through a new `@if(NAME,NAME)` gate (set-secrets.sh) only when GitHub secrets `NOTIFICATIONS_SMTP_HOST` and `NOTIFICATIONS_SMTP_FROM` exist; `booking.fly.toml` no longer sets the provider (a secret and an `[env]` line of the same name must not compete), so the code default `Log` applies otherwise. All six SMTP values are GitHub *secrets* (the deploy reads the secrets context only), not variables.
**Honesty.** Outside the fake profile a `Log` provider is announced: one startup warning and `email: Degraded` in `/health/details` (`NotificationsFeature`; tests in `SmtpWiringTests`). The fake profile stays quiet.

**Decision: the model.** The code binds `Llm:Provider` (`Copilot` | `Fake`: there is no Null provider), `Llm:CustomModels:N:*` and `Copilot:GitHubToken`. `assistant.fly.toml` keeps `Copilot`: with no credentials the gateway reports `NotAuthenticated`/`Unavailable` and the Assistant answers `unavailable` (existing tests: `TriageTests`, `BriefTests`; web: `BookingFormTests`, 503 `assistant_unavailable` shows the plain form). One custom model is mapped (`LLM_CUSTOM_ENDPOINT`, `LLM_CUSTOM_MODEL`, `LLM_CUSTOM_API_KEY`, `LLM_CUSTOM_PROVIDER_TYPE`, `LLM_CUSTOM_WIRE_API`), all-or-nothing (a half entry fails `LlmOptionsValidator` and stops the host), plus `COPILOT_GITHUB_TOKEN`. **Finding for the owner:** `CopilotLlmGateway.StartAsync` requires a signed-in Copilot runtime before it lists custom models, so a bring-your-own-key model alone does not make the Assistant ready; it needs the Copilot token (A-04 terms decision) too. Not changed here (it is code behaviour of T-09.07).

**Config coverage.** `deploy/config-keys.map` + `scripts/check-config-coverage.sh` (CI deploy-checks): required keys need a source (`[env]` or a secrets row), fake-only keys must have none, and every key the code reads must be classified. First run found no missing source.

## 10. The Blazor host's auth against the real authservice (branch `fix/blazor-real-auth`, 2026-11-22)

The cutover deleted the only test of the web's auth handlers against the real authservice (`real-authservice.test.ts`). `tests/Gabinety.Integration.Tests/BlazorAuth` replaces it for the Blazor host (list in `docs/operations/AUTH.md` section 9).

- **In process, no fake backend.** `WebApplicationFactory<Program>` of `Gabinety.Web` (extern alias `WebApp`, as the services in the same project) with `Backend__Auth`/`Identity__PublicUrl`/issuer/audience of the real instance; the other backends point at a closed port because the auth pages never call them. A "deploy" is a second host with the same `DataProtection:KeysPath` (an empty session cache, the same cookies); the clock is an offset `TimeProvider`, so an hour-long access token is "about to expire" without waiting.
- **Same collection and fixture as `RealAuth`** (one PostgreSQL, the `Main`/`Confirm` instances); key rotation starts two more instances on one database and moves the host between them with a rewriting handler.
- **Failure power** (mutation runs, each fails exactly the test named): removing the keyed lock of `UserTokens` fails the concurrent-refresh test; dropping the cookie catch-up fails the rotation test; `IsAdmin` without `SuperAdmin` fails the owner test; keeping only the first role fails the array test; dropping `Fly-Client-IP` fails the two-visitors test; skipping the identity logout fails the logout test; `HttpOnly=false` fails the cookie test.
- **Result:** no defect found in the Blazor auth code; nothing was changed in `src/`. Run here against the v0.3.4 source as a process (AUTH.md section 10); the image run is the CI `integration` job.
- **Not proven, stays on the owner's list:** the GitHub callback leg with a real OAuth app (GO-LIVE row 5) and e-mail confirmation delivery.
- **Required checks** are `e2e`, `build-test`, `docker-build-backend (web)`; CI.md and GO-LIVE row 11 said otherwise.
