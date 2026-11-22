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
