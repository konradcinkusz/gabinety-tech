# PARITY: Next.js routes against the Blazor host (ADR-0010)

The parity checklist the **cutover PR gate reads** (`docs/operations/CUTOVER.md`, step 1). Two hosts run side by side until the cutover: Next.js (`web/`, :3000) and
the Blazor Web App (`src/Gabinety.Web`, :3001 in the fake profile). A Next route with no Blazor route (and no exemption) blocks the cutover.

**Enforced by** `scripts/check-parity.mjs`: it enumerates the page and route files of `web/src/app` and the `@page`, `MapGet`/`MapPost` and `...Path` constants of `src/Gabinety.Web`,
compares them (dynamic segments by position, not name) and prints what is missing. Report only by default; with `CUTOVER_GATE=1` (the repository variable of the same name in CI,
set by the cutover PR) it exits 1 on any missing route. Exemptions live in `scripts/parity-exemptions.json`, each with a reason. This table is the human view of the same facts and the
owners; the script is authoritative, and the CI step prints its output on every run of the `e2e-blazor` job.

**Journeys** J1-J7 run against both hosts (`HOST=next|blazor`, `tests/e2e`). On the Blazor host a journey whose page is not ported is **skipped** with the reason
`not ported yet: <route> (owner <branch>; docs/operations/PARITY.md)`; the probe is live, so the skip disappears when the page lands. The cutover gate runs the journeys with
`PARITY_STRICT=1`, which makes any such skip a failure. The Next routes under `/api/` (the BFF) have no counterpart by design (exempt: ADR-0010 decision 4).

Status as of this table's last update; **owners update the row in the PR that ports the page** (and run `node scripts/check-parity.mjs` to see the truth).

| Next route | Blazor route | Owner branch | J-hooks | Status |
|---|---|---|---|---|
| `/` | `/` | port/public | smoke | ported |
| `/admin/ksiega` | - | port/admin | J6 | not ported |
| `/admin/ksiega/csv` | - | port/admin | J6 | not ported |
| `/admin/opinie` | - | port/admin | - | not ported |
| `/admin/podejrzane` | - | port/admin | - | not ported |
| `/admin/spory` | - | port/admin | J6 | not ported |
| `/admin/weryfikacja` | - | port/admin | J2 | not ported |
| `/admin/weryfikacja/[id]` | - | port/admin | J2 | not ported |
| `/cennik` | - | port/public | - | not ported |
| `/checkout/mock/[bookingId]` | - | port/booking | J3, J4 | not ported |
| `/gabinet-programisty` | - | port/practitioner | J2 | not ported |
| `/gabinet-programisty/dostepnosc` | - | port/practitioner | - | not ported |
| `/gabinet-programisty/oferty` | - | port/practitioner | J2 | not ported |
| `/gabinet-programisty/profil` | - | port/practitioner | J2 | not ported |
| `/gabinet/[slug]` | - | port/public | J1, J2, J3, J4 | not ported |
| `/jak-to-dziala` | - | port/public | - | not ported |
| `/konto` | - | port/auth | - | not ported |
| `/konto/export` | - | port/auth | - | not ported |
| `/logowanie` | `/logowanie` | port/auth | all (sign-in form) | ported |
| `/moje/opinie` | - | port/booking | - | not ported |
| `/moje/opinie/[bookingId]` | - | port/booking | J3 | not ported |
| `/moje/rezerwacje` | `/moje/rezerwacje` | port/booking | J3, smoke | ported |
| `/moje/rezerwacje/[id]` | - | port/booking | J3, J5, J6 | not ported |
| `/oauth/callback` | `/oauth/callback` | port/auth | - | ported |
| `/polityka-prywatnosci` | - | port/public | - | not ported |
| `/programisci` | `/programisci` | port/public | J1, J2, smoke | ported |
| `/programisci/[problem]` | `/programisci/{Problem}` | port/public | J1 | ported |
| `/programisci/[problem]/[tech]` | `/programisci/{Problem}/{Tech}` | port/public | - | ported |
| `/regulamin` | - | port/public | - | not ported |
| `/rejestracja` | - | port/auth | J2 | not ported |
| `/rezerwacja/[offeringId]` | - | port/booking | J3, J4 | not ported |
| `/rezerwacja/potwierdzenie/[bookingId]` | - | port/booking | - | not ported |
| `/robots.txt` | - | port/public | - | not ported |
| `/sitemap.xml` | - | port/public | - | not ported |
| `/zasady-rankingu` | - | port/public | - | not ported |

Not routes, but parity all the same (cutover preconditions of ADR-0010): the stateless session cookie (deploys must not sign users out), the five-minute output cache of public pages,
password rules on registration, and the J7 Blazor variant (a foreign `Origin` refused with `403 cross_site_request` against `/logowanie`; owner `port/auth`). J7 is skipped on the Blazor host
until that variant lands; `OriginGuardHostTests` pin the guard in-process meanwhile.
