# Route table and authorization (T-10.12, F-06)

Every route of the four services, with the policy the code declares for it. `RouteAuthTests` (one per service, `tests/Gabinety.*.Tests`) enumerates the
endpoints of the running host and fails on any route or policy that differs from this file, and on any route with no explicit policy.

**To add or change a route:** add or edit its row here in the same PR, and say why in the PR description. A new route needs
`RequireAuthorization("<policy>")` or an explicit `AllowAnonymous()`; a route with neither is refused by the test and is 401 at runtime
(fallback policy: `Security.RequireAuthenticatedByDefault`, applied in each `Program.cs`).

Columns: **Policy** = what the code declares (checked by the test): `Anonymous`, `Authenticated` (signed-in user, JWT), `Admin` (role `Admin` or `SuperAdmin`),
`Internal` (only scheme `InternalKey`; one key per callee, ADR-0007), `Role:SuperAdmin` is added on top of the group's `Admin`.
**Profile** = `all` (every deployment) or `fake` (mapped only when `GABINETY_FAKE=1` or `Environment:Kind=Development`; absent in a deployed profile, checked by the test).
**Note** is for people and is not checked (callers of an internal route are SPEC 5.1; the code does not tie a key to a caller, security review F-03).

## Directory

| Service | Method | Route | Policy | Profile | Note |
|---|---|---|---|---|---|
| Directory | ANY | `/health` | Anonymous | all | liveness/readiness |
| Directory | ANY | `/alive` | Anonymous | all | liveness |
| Directory | GET | `/health/details` | Anonymous | all | F-07 |
| Directory | GET | `/openapi/v1.json` | Anonymous | all | F-07 |
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
| Booking | GET | `/openapi/v1.json` | Anonymous | all | F-07 |
| Booking | GET | `/api/v1/practitioners/{practitionerId}/slots` | Anonymous | all | public read |
| Booking | POST | `/webhooks/payments/{provider}` | Anonymous | all | guarded by the provider signature |
| Booking | POST | `/api/v1/payments/simulate` | Authenticated | all | Mock provider only (F-01) |
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
| Reputation | GET | `/openapi/v1.json` | Anonymous | all | F-07 |
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
| Assistant | GET | `/openapi/v1.json` | Anonymous | all | F-07 |
| Assistant | GET | `/api/v1/assistant/status` | Anonymous | all | CONTRACTS 6 |
| Assistant | POST | `/api/v1/assistant/brief` | Authenticated | all | own limiter |
| Assistant | POST | `/internal/v1/assistant/review-triage` | Internal | all | caller: Reputation |
