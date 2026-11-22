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

## Wave 2, public pages of the Blazor host (branch `port/public-pages`, 2026-11-22)

**Decisions** (supervisor session; revisable). (1) **Slot picker without JavaScript**: the profile renders each offer's free times in the first response (one Booking read per 14-day window, anonymous). A strip of days with free times, the times of ONE day, "Pokaż kolejne dni" are plain links whose state is in the URL (`?okna=`, `?oferta=&dzien=`), so the page works with scripts off and is cached by route and query. A time links to `/rezerwacja/{offering}?start=<utc>&gabinet=<slug>` exactly as the Next.js page does (the task text said `slot=`; the Next.js page and the journeys use `start`). (2) **Reviews** are the first page rendered in the HTML with a "more" link (`?opinie=<cursor>`), not a client fetch. (3) **F-05**: the Directory pages by cursor and has no total, so a filtered or paged variant states a count only when its page is the whole result, and otherwise none; the unfiltered category keeps the sitemap's count (journey J1 reads `Wyniki: N`). (4) **404 of a profile** is the router's not-found page, which says "Nie znaleziono programisty" for `/gabinet/...` (the status-code re-execution does not let a page that sets 404 itself render its own body); a `410` from the Directory is a 410 with the same copy. (5) **Output cache**: `OutputCache:Enabled` (default true; the AppHost's Blazor host sets it false for the journeys) and `OutputCache:Seconds` (default 300). The policy stores only a plain `200` without `Set-Cookie`, and refuses a request with the `gt_session` cookie or an `Authorization` header; a cached page is therefore never personal. (6) **Legal text** is copied to `src/Gabinety.Web/Content/Legal` (embedded); a test pins both copies identical until the cutover moves them. (7) **Contents list** on legal pages lists the file's `##` sections. (8) **Polish text** lives in `Text/*.cs` (the source-rule test now allows the whole folder).

**Not decided here.** Sort control of the list (the Directory has no sort parameter), the phone-collapsed `Filtry (n)` (a `<details>` is open by default because collapsing at a breakpoint needs script), the price range in the home trust row (needs a Directory read of its own).

## 6. J3 flake: the review-to-Directory push waited for a 60 s timer (branch `fix/j3-flake`, 2026-11-22)

**Symptom.** J3 failed about one run in three with `the third review reaches the Directory  Expected: 3  Received: 0` (PRs #124 and #132; they passed on rerun).

**Root cause.** Reputation pushes a rating summary to the Directory only from `SummaryPushHostedService`, a `PeriodicTimer` of 60 s on the real timer (`OffsetTimeProvider` hands timers to the system provider, so the harness's clock jumps do not move it). A review committed just after a tick therefore reaches the Directory up to a whole interval later, and J3 waited exactly 60 s (`expect.poll` timeout 60 000 ms): the wait budget equalled the period, with no room for the push call itself. It was one in three, not rare, because the suite's timing is nearly the same every run (the AppHost starts, the seeder finishes, J1 and J2 run, J3 books and completes), so the third review kept landing within seconds of the same point of the timer's grid. In the failed job the poll window opened at about 10:48:36 and the timer's grid, set by Reputation's start at about 10:44:36, has a tick at :36 of each minute.
Ruled out by reading the code: the version ordering (Reputation's version is a per-practitioner counter, the seeder's baseline 1 only swallows the first review's push, and the third review's version is higher than anything stored), the `StatsPushJob` (it pushes booking stats, not the rating, and its timestamp version is a separate column), the seeder (it pushes version 1 once, before the tests), the clock offset (not read by the job's timer), a lease or lock (none; the job has an in-process gate only), and the review being created before the booking is Completed (the create call checks Booking's reviewability and answered Published). The CI log carries no service output, so the grid argument is an inference from timestamps and the code, not an observation of the job; the fix removes the dependency on the grid, which is what makes the test deterministic.

**Decision.** Fix the product, not the test: a published review now signals the push loop (`SummaryPushSignal`, a coalescing one-slot channel), which runs a cycle at once; the 60 s timer stays as the safety net (an unpushed summary, a Directory outage, a restart). The test's budget was not raised and nothing retries or sleeps. The lag a reviewer's rating takes to appear on the profile drops from 0 to 60 s to the length of one HTTP call. Only the create path signals; DemoReset and the reply path change no rating the Directory shows, and the timer still carries any other change.

**Proof.** `SummaryPushPromptnessTests`: a published review is pushed with no tick (the host's clock is manual and the real timer is 60 s); fails when the `Notify()` call is removed (mutation run, the push list stays empty); a Held review pushes nothing.
