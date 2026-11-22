# Route table and authorization (T-10.12, F-06)

Every route of the four services, with the policy the code declares for it. `RouteAuthTests` (one per service, `tests/Gabinety.*.Tests`) enumerates the
endpoints of the running host and fails on any route or policy that differs from this file, and on any route with no explicit policy.

**To add or change a route:** add or edit its row here in the same PR, and say why in the PR description. A new route needs
`RequireAuthorization("<policy>")` or an explicit `AllowAnonymous()`; a route with neither is refused by the test and is 401 at runtime
(fallback policy: `Security.RequireAuthenticatedByDefault`, applied in each `Program.cs`).

Columns: **Policy** = what the code declares (checked by the test): `Anonymous`, `Authenticated` (signed-in user, JWT), `Admin` (role `Admin` or `SuperAdmin`),
`Internal` (only scheme `InternalKey`; one key per caller and callee pair, `Internal:Callers:<Caller>`, recorded as the `caller` claim; ADR-0007, F-03), `Role:SuperAdmin` is added on top of the group's `Admin`.
**Profile** = `all` (every deployment) or `fake` (mapped only when `GABINETY_FAKE=1` or `Environment:Kind=Development`; absent in a deployed profile, checked by the test).
**Note** is for people and is not checked (callers of an internal route are SPEC 5.1; each caller has its own key on the callee, F-03).

**Public edge (F-03, `Gabinety.ServiceDefaults.Edge`, not a policy so not checked by this table):** a request that arrived through the Fly public edge, or that carries `X-Edge-Key`, never reaches an `/internal/*` row below: it is 404 before authentication, whatever key it carries (default on, off in the fake profile). Booking, the one public app, also answers 404 from the edge to everything except `POST /webhooks/payments/{provider}`, `/alive` and `/api/v1/*` other than `/api/v1/admin/*` (`src/Gabinety.Booking/PublicEdge.cs`). No route was added or changed by T-10.10.

## Directory

| Service | Method | Route | Policy | Profile | Note |
|---|---|---|---|---|---|
| Directory | ANY | `/health` | Anonymous | all | liveness/readiness |
| Directory | ANY | `/alive` | Anonymous | all | liveness |
| Directory | GET | `/health/details` | Anonymous | all | F-07 |
| Directory | GET | `/openapi/v1.json` | Anonymous | fake | F-07: served only in the fake profile |
| Directory | GET | `/api/v1/taxonomy` | Anonymous | all | public read |
| Directory | GET | `/api/v1/ranking-rules` | Anonymous | all | public read |
| Directory | GET | `/api/v1/practitioners` | Anonymous | all | public search |
| Directory | GET | `/api/v1/practitioners/{slug}` | Anonymous | all | Approved only |
| Directory | GET | `/api/v1/sitemap` | Anonymous | all | public read |
| Directory | GET | `/api/v1/me` | Authenticated | all | claims of the caller |
| Directory | POST | `/api/v1/me/practitioner` | Authenticated | all | own row by `sub` |
| Directory | GET | `/api/v1/me/practitioner` | Authenticated | all | |
| Directory | PUT | `/api/v1/me/practitioner` | Authenticated | all | |
| Directory | POST | `/api/v1/me/practitioner/submit` | Authenticated | all | |
| Directory | GET | `/api/v1/me/practitioner/offerings` | Authenticated | all | |
| Directory | POST | `/api/v1/me/practitioner/offerings` | Authenticated | all | |
| Directory | PUT | `/api/v1/me/practitioner/offerings/{id}` | Authenticated | all | |
| Directory | POST | `/api/v1/me/practitioner/offerings/{id}/deactivate` | Authenticated | all | |
| Directory | POST | `/api/v1/me/practitioner/offerings/{id}/activate` | Authenticated | all | undo of deactivate; 422 `too_many_offerings` at 5 active |
| Directory | POST | `/api/v1/me/practitioner/github-link` | Authenticated | all | |
| Directory | POST | `/api/v1/me/practitioner/github-link/verify` | Authenticated | all | |
| Directory | GET | `/api/v1/admin/practitioners` | Admin | all | |
| Directory | GET | `/api/v1/admin/practitioners/{id}` | Admin | all | |
| Directory | GET | `/api/v1/admin/practitioners/{id}/verification-events` | Admin | all | |
| Directory | POST | `/api/v1/admin/practitioners/{id}/approve` | Admin | all | |
| Directory | POST | `/api/v1/admin/practitioners/{id}/reject` | Admin | all | |
| Directory | POST | `/api/v1/admin/practitioners/{id}/suspend` | Admin | all | |
| Directory | POST | `/api/v1/admin/practitioners/{id}/reinstate` | Admin | all | |
| Directory | PUT | `/api/v1/admin/moderators/{userId}` | Admin+Role:SuperAdmin | all | |
| Directory | DELETE | `/api/v1/admin/moderators/{userId}` | Admin+Role:SuperAdmin | all | |
| Directory | GET | `/internal/v1/offerings/{id}` | Internal | all | callers: Booking, Reputation |
| Directory | GET | `/internal/v1/practitioners/by-user/{userId}` | Internal | all | callers: Booking, Reputation |
| Directory | GET | `/internal/v1/practitioners/{id}` | Internal | all | caller: Booking (display name and slug for the booking views, ADR-0012 section 6) |
| Directory | GET | `/internal/v1/practitioners/{userId}/contact` | Internal | all | caller: Booking (T-03.09, notifications): the e-mail snapshot of the practitioner, `404` when none is held. Booking already holds `Internal__Keys__Directory`: no new key |
| Directory | PUT | `/internal/v1/practitioners/{id}/reputation` | Internal | all | caller: Reputation |
| Directory | PUT | `/internal/v1/practitioners/{id}/booking-stats` | Internal | all | caller: Booking |
| Directory | DELETE | `/internal/v1/subjects/{userId}` | Internal | all | owner runbook (erasure) |
| Directory | POST | `/internal/v1/demo/reset` | Internal | fake | owner runbook / seeder |

## Booking

| Service | Method | Route | Policy | Profile | Note |
|---|---|---|---|---|---|
| Booking | ANY | `/health` | Anonymous | all | |
| Booking | ANY | `/alive` | Anonymous | all | |
| Booking | GET | `/health/details` | Anonymous | all | F-07 |
| Booking | GET | `/openapi/v1.json` | Anonymous | fake | F-07: served only in the fake profile |
| Booking | GET | `/api/v1/practitioners/{practitionerId}/slots` | Anonymous | all | public read |
| Booking | POST | `/webhooks/payments/{provider}` | Anonymous | all | guarded by the provider signature |
| Booking | POST | `/api/v1/payments/simulate` | Authenticated | fake | Mock provider only, and only the booking's client (F-01, T-10.08); not mapped when deployed |
| Booking | GET | `/api/v1/me/payments/account` | Authenticated | all | practitioner only; 404 when the provider has no connected accounts (Mock); T-09.05 |
| Booking | POST | `/api/v1/me/payments/account/onboarding` | Authenticated | all | practitioner only; creates the connected account and returns a single-use onboarding link; T-09.05 |
| Booking | GET | `/api/v1/me/availability` | Authenticated | all | |
| Booking | PUT | `/api/v1/me/availability` | Authenticated | all | |
| Booking | POST | `/api/v1/bookings` | Authenticated | all | |
| Booking | GET | `/api/v1/bookings/{id}` | Authenticated | all | |
| Booking | GET | `/api/v1/me/bookings` | Authenticated | all | |
| Booking | POST | `/api/v1/bookings/{id}/cancel` | Authenticated | all | |
| Booking | POST | `/api/v1/bookings/{id}/dispute` | Authenticated | all | |
| Booking | POST | `/api/v1/bookings/{id}/report-client-no-show` | Authenticated | all | |
| Booking | GET | `/api/v1/admin/disputes` | Admin | all | |
| Booking | POST | `/api/v1/admin/bookings/{id}/resolve` | Admin | all | |
| Booking | GET | `/api/v1/admin/ledger.csv` | Admin | all | |
| Booking | GET | `/internal/v1/bookings/{id}/reviewability` | Internal | all | caller: Reputation |
| Booking | POST | `/internal/v1/practitioners/{id}/cancel-future-bookings` | Internal | all | caller: Directory (suspension and erasure, ADR-0012); idempotent |
| Booking | DELETE | `/internal/v1/subjects/{userId}` | Internal | all | owner runbook (erasure) |
| Booking | GET | `/internal/v1/analytics/pair-counts` | Internal | all | caller: Reputation (T-10.03) |
| Booking | GET | `/internal/v1/test/clock` | Internal | fake | owner / seeder |
| Booking | POST | `/internal/v1/test/clock` | Internal | fake | owner / seeder |
| Booking | POST | `/internal/v1/demo/reset` | Internal | fake | owner / seeder |

## Reputation

| Service | Method | Route | Policy | Profile | Note |
|---|---|---|---|---|---|
| Reputation | ANY | `/health` | Anonymous | all | |
| Reputation | ANY | `/alive` | Anonymous | all | |
| Reputation | GET | `/health/details` | Anonymous | all | F-07 |
| Reputation | GET | `/openapi/v1.json` | Anonymous | fake | F-07: served only in the fake profile |
| Reputation | GET | `/api/v1/practitioners/{practitionerId}/reviews` | Anonymous | all | Published only |
| Reputation | POST | `/api/v1/reviews` | Authenticated | all | |
| Reputation | POST | `/api/v1/reviews/{id}/reply` | Authenticated | all | |
| Reputation | GET | `/api/v1/admin/reviews` | Admin | all | |
| Reputation | POST | `/api/v1/admin/reviews/{id}/publish` | Admin | all | |
| Reputation | POST | `/api/v1/admin/reviews/{id}/reject` | Admin | all | |
| Reputation | GET | `/api/v1/admin/suspicious-pairs` | Admin | all | T-10.03 |
| Reputation | DELETE | `/internal/v1/subjects/{userId}` | Internal | all | owner runbook (erasure) |
| Reputation | GET | `/internal/v1/test/clock` | Internal | fake | owner / seeder |
| Reputation | POST | `/internal/v1/test/clock` | Internal | fake | owner / seeder |
| Reputation | POST | `/internal/v1/demo/reset` | Internal | fake | owner / seeder |

## Assistant

| Service | Method | Route | Policy | Profile | Note |
|---|---|---|---|---|---|
| Assistant | ANY | `/health` | Anonymous | all | |
| Assistant | ANY | `/alive` | Anonymous | all | |
| Assistant | GET | `/health/details` | Anonymous | all | F-07 |
| Assistant | GET | `/openapi/v1.json` | Anonymous | fake | F-07: served only in the fake profile |
| Assistant | GET | `/api/v1/assistant/status` | Anonymous | all | CONTRACTS 6 |
| Assistant | POST | `/api/v1/assistant/brief` | Authenticated | all | own limiter |
| Assistant | POST | `/internal/v1/assistant/review-triage` | Internal | all | caller: Reputation |

## Web (the Blazor host, `src/Gabinety.Web`, ADR-0010 proposal)

The Blazor web host is not one of the four services, but it is held to the same rule: deny by default (`Security.RequireAuthenticatedByDefault`), a route is public only when it says so, and `tests/Gabinety.Web.Tests` (`RouteAuditTests`) fails on any route or policy that differs from this table.
`PAGE` is a Razor component endpoint (it answers GET, and POST for its forms). Component pages carry their policy as an attribute (`@attribute [AllowAnonymous]` or `@attribute [Authorize]`); the framework's circuit endpoints (`/_blazor/*`, `/_framework/*`) are marked anonymous by one convention in `WebHostingExtensions`, never by a blanket `AllowAnonymous` on the component endpoints (that would override `[Authorize]`).
Static assets (the stylesheets, the framework script) are mapped by `MapStaticAssets().AllowAnonymous()`; their fingerprinted names change with their content, so the test checks them as a group (all anonymous) and they are not listed.
The middleware is UX, the services are the boundary: a signed-in page still calls the backend with the user's bearer, and the backend enforces its own policy (SPEC 11).

| Service | Method | Route | Policy | Profile | Note |
|---|---|---|---|---|---|
| Web | ANY | `/health` | Anonymous | all | liveness/readiness (kernel) |
| Web | ANY | `/alive` | Anonymous | all | liveness (kernel) |
| Web | PAGE | `/` | Anonymous | all | home, static server rendering |
| Web | PAGE | `/programisci` | Anonymous | all | public list, static server rendering |
| Web | PAGE | `/programisci/{Problem}` | Anonymous | all | public list of a category |
| Web | PAGE | `/programisci/{Problem}/{Tech}` | Anonymous | all | public list of a category and a technology |
| Web | PAGE | `/gabinet/{Slug}` | Anonymous | all | public profile with JSON-LD, slot picker and reviews; 404/410/503 as statuses |
| Web | PAGE | `/jak-to-dziala` | Anonymous | all | static page |
| Web | PAGE | `/cennik` | Anonymous | all | static page, fee rate from configuration |
| Web | PAGE | `/zasady-rankingu` | Anonymous | all | ranking rules from the Directory; 503 when it does not answer |
| Web | PAGE | `/regulamin` | Anonymous | all | legal text (embedded markdown) |
| Web | PAGE | `/polityka-prywatnosci` | Anonymous | all | legal text (embedded markdown) |
| Web | GET | `/sitemap.xml` | Anonymous | all | approved practitioners and indexable categories; 503 when the Directory does not answer |
| Web | GET | `/robots.txt` | Anonymous | all | the rules of the retired Next.js app (the sitemap and robots of ADR-0010) |
| Web | PAGE | `/logowanie` | Anonymous | all | sign-in form (the POST is handled by the page, antiforgery) |
| Web | PAGE | `/logowanie/kod` | Anonymous | all | second step of a two-factor sign-in; needs the sealed `gt_2fa` challenge cookie, else back to `/logowanie` |
| Web | PAGE | `/rejestracja` | Anonymous | all | registration form (the POST is handled by the page, antiforgery) |
| Web | PAGE | `/rejestracja/sprawdz-skrzynke` | Anonymous | all | "confirm your e-mail" state after a `202` registration |
| Web | PAGE | `/wyloguj` | Anonymous | all | sign-out form |
| Web | GET | `/oauth/github/start` | Anonymous | all | GitHub sign-in start: state cookie `gt_oauth_state` + returnUrl from `Site:BaseUrl` (PR #118) |
| Web | GET | `/oauth/callback` | Anonymous | all | GitHub callback carve-out (PR #118): no sign-in redirect, state checked against the cookie, code exchanged |
| Web | PAGE | `/admin` | Admin | all | administrator landing; a signed-in non-admin gets 403 "Brak dostępu", a visitor goes to sign-in (F-10) |
| Web | PAGE | `/admin/weryfikacja` | Admin | all | verification queue; approve, reject, suspend, reinstate are antiforgery posts, the services check the role again |
| Web | PAGE | `/admin/weryfikacja/{Id}` | Admin | all | one profile and its verification history (read only) |
| Web | PAGE | `/admin/spory` | Admin | all | disputes; resolve is an antiforgery post with a confirmation step |
| Web | PAGE | `/admin/opinie` | Admin | all | review moderation; publish and reject are antiforgery posts |
| Web | PAGE | `/admin/ksiega` | Admin | all | ledger by period, first rows as a table |
| Web | GET | `/admin/ksiega/csv` | Admin | all | ledger CSV, STREAMED from Booking with the administrator's own bearer (nothing buffered); an error mid-stream aborts the connection |
| Web | PAGE | `/admin/podejrzane` | Admin | all | suspicious client-practitioner pairs (T-10.03) |
| Web | PAGE | `/moje/rezerwacje` | Authenticated | all | my bookings (client and practitioner tabs), static server rendering |
| Web | PAGE | `/moje/rezerwacje/{BookingId}` | Authenticated | all | booking detail with cancel, dispute and no-show (form posts, antiforgery) |
| Web | GET | `/moje/rezerwacje/{BookingId}/kalendarz.ics` | Authenticated | all | the booking as an iCalendar attachment (`text/calendar`, no-store), read with the user's own bearer; only for Confirmed or Completed bookings, 404 otherwise and for another user's booking |
| Web | PAGE | `/moje/opinie` | Authenticated | all | the practitioner's own reviews and replies |
| Web | PAGE | `/moje/opinie/{BookingId}` | Authenticated | all | write a review (form post, antiforgery) |
| Web | PAGE | `/rezerwacja/{OfferingId}` | Authenticated | all | booking form: brief, assistant suggestion, pay (form posts, antiforgery) |
| Web | PAGE | `/rezerwacja/potwierdzenie/{BookingId}` | Authenticated | all | confirmation |
| Web | PAGE | `/checkout/mock/{BookingId}` | Authenticated | all | mock checkout: answers 404 unless the fake profile runs the mock provider (`WebOptions.MockPayments`); the clock alone is interactive |
| Web | PAGE | `/bookings/{BookingId}` | Authenticated | all | where the payment provider returns the browser (Stripe Checkout success URL): redirects to the confirmation |
| Web | PAGE | `/konto` | Authenticated | all | account: e-mail, consents, data export link, account deletion (static rendering, forms with antiforgery) |
| Web | GET | `/konto/export` | Authenticated | all | the user's data as a no-store JSON attachment (the identity service's export, passed through) |
| Web | PAGE | `/gabinet-programisty` | Authenticated | all | practitioner dashboard and onboarding, static server rendering; any signed-in account may become a practitioner (ADR-0012, "Practitioner area") |
| Web | PAGE | `/gabinet-programisty/profil` | Authenticated | all | practitioner profile form |
| Web | PAGE | `/gabinet-programisty/oferty` | Authenticated | all | offers: list, confirmation before disabling, re-enable, form |
| Web | PAGE | `/gabinet-programisty/dostepnosc` | Authenticated | all | weekly availability and blocked days |
| Web | PAGE | `/gabinet-programisty/platnosci` | Authenticated | all | payments account status and onboarding link (PAYMENTS-GO-LIVE) |
| Web | PAGE | `/practitioner/payments` | Authenticated | all | the return and refresh address Booking gives Stripe; forwards to `/gabinet-programisty/platnosci` |
| Web | PAGE | `/not-found` | Anonymous | all | status code re-execution target |
| Web | PAGE | `/Error` | Anonymous | all | exception handler target |
| Web | ANY | `/_blazor` | Anonymous | all | circuit websocket: refused first by the origin guard for a foreign page |
| Web | ANY | `/_blazor/negotiate` | Anonymous | all | circuit negotiation |
| Web | ANY | `/_blazor/disconnect` | Anonymous | all | circuit |
| Web | ANY | `/_blazor/initializers` | Anonymous | all | circuit |
| Web | GET | `/_framework/opaque-redirect` | Anonymous | all | framework |
| Web | GET | `/{**path:file}` | Anonymous | all | the static-asset fallback for files under `wwwroot` (public files, never data); anonymous through the group in `MapWebEndpoints` |
| Web | HEAD | `/{**path:file}` | Anonymous | all | as above |
