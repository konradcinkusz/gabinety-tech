# PARITY: done on 2026-11-22

The Next.js frontend (`web/`) was retired by the cutover PR (`docs/operations/CUTOVER.md`, sections 0-1); `src/Gabinety.Web` (the Blazor Web App, ADR-0010) is the only web host.
This file is the archive of the gate that preceded it. The gate itself (`scripts/check-parity.mjs`, its test, `scripts/parity-exemptions.json`, the `CUTOVER_GATE` repository variable, the
`HOST=next|blazor` switch and `PARITY_STRICT` of the e2e harness) was deleted with `web/`: there is nothing left to compare. It lives in the git history before the cutover commit.

**Result at the gate** (`CUTOVER_GATE=1 node scripts/check-parity.mjs`): 44 Next routes, 35 ported, 9 exempt, 0 missing. Journeys J1-J7 passed on the Blazor host with no skips
(`HOST=blazor PARITY_STRICT=1`, 18 tests).

**The one exemption.** `/api/*`, the Next BFF (`/api/auth`, `/api/proxy`, `/api/config`, `/api/health`): no counterpart by design. The Blazor host never lets the browser call a backend
(ADR-0010 decision 4); its health route is the kernel's `/health`.

**Route map** (Next route to Blazor route; every row was ported before the cutover):

| Next route | Blazor route | Owner branch | J-hooks | Status |
|---|---|---|---|---|
| `/` | `/` | port/public | smoke | ported |
| `/admin/ksiega` | `/admin/ksiega` | port/admin | J6 | ported |
| `/admin/ksiega/csv` | `/admin/ksiega/csv` | port/admin | J6 | ported |
| `/admin/opinie` | `/admin/opinie` | port/admin | - | ported |
| `/admin/podejrzane` | `/admin/podejrzane` | port/admin | - | ported |
| `/admin/spory` | `/admin/spory` | port/admin | J6 | ported |
| `/admin/weryfikacja` | `/admin/weryfikacja` | port/admin | J2 | ported |
| `/admin/weryfikacja/[id]` | `/admin/weryfikacja/{Id:guid}` | port/admin | J2 | ported |
| `/cennik` | `/cennik` | port/public | - | ported |
| `/checkout/mock/[bookingId]` | `/checkout/mock/{BookingId:guid}` | port/booking | J3, J4 | ported |
| `/gabinet-programisty` | `/gabinet-programisty` | port/practitioner | J2 | ported |
| `/gabinet-programisty/dostepnosc` | `/gabinet-programisty/dostepnosc` | port/practitioner | - | ported |
| `/gabinet-programisty/oferty` | `/gabinet-programisty/oferty` | port/practitioner | J2 | ported |
| `/gabinet-programisty/profil` | `/gabinet-programisty/profil` | port/practitioner | J2 | ported |
| `/gabinet/[slug]` | `/gabinet/{Slug}` | port/public | J1, J2, J3, J4 | ported |
| `/jak-to-dziala` | `/jak-to-dziala` | port/public | - | ported |
| `/konto` | `/konto` | port/auth | - | ported |
| `/konto/export` | `/konto/export` | port/auth | - | ported |
| `/logowanie` | `/logowanie` | port/auth | all (sign-in form) | ported |
| `/moje/opinie` | `/moje/opinie` | port/booking | - | ported |
| `/moje/opinie/[bookingId]` | `/moje/opinie/{BookingId:guid}` | port/booking | J3 | ported |
| `/moje/rezerwacje` | `/moje/rezerwacje` | port/booking | J3, smoke | ported |
| `/moje/rezerwacje/[id]` | `/moje/rezerwacje/{BookingId:guid}` | port/booking | J3, J5, J6 | ported |
| `/oauth/callback` | `/oauth/callback` | port/auth | - | ported |
| `/polityka-prywatnosci` | `/polityka-prywatnosci` | port/public | - | ported |
| `/programisci` | `/programisci` | port/public | J1, J2, smoke | ported |
| `/programisci/[problem]` | `/programisci/{Problem}` | port/public | J1 | ported |
| `/programisci/[problem]/[tech]` | `/programisci/{Problem}/{Tech}` | port/public | - | ported |
| `/regulamin` | `/regulamin` | port/public | - | ported |
| `/rejestracja` | `/rejestracja` | port/auth | J2 | ported |
| `/rezerwacja/[offeringId]` | `/rezerwacja/{OfferingId:guid}` | port/booking | J3, J4 | ported |
| `/rezerwacja/potwierdzenie/[bookingId]` | `/rezerwacja/potwierdzenie/{BookingId:guid}` | port/booking | - | ported |
| `/robots.txt` | `/robots.txt` | port/public | - | ported |
| `/sitemap.xml` | `/sitemap.xml` | port/public | - | ported |
| `/zasady-rankingu` | `/zasady-rankingu` | port/public | - | ported |

Not routes, but parity all the same (cutover preconditions of ADR-0010), all met before the cutover: the stateless session cookie (deploys do not sign users out), the five-minute output cache of
the public pages, password rules on registration, the J7 variant against `/logowanie` (`OriginGuardHostTests` pin the guard in-process as well).
