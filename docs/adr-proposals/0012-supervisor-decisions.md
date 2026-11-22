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
