# Getting started

How to run Gabinety Tech on your machine, sign in, click through every journey (J1 to J7), pass business time, and run each test layer.

Every command below was run on Linux with the .NET SDK 10.0.404 (`global.json` asks for 10.0.100 or a later 10.0 feature band), Node 22 and pnpm 10, on 2026-11-17. Where a result is quoted it is what the command printed. Section 8 lists what could not be run in that environment.

## 1. Start the fake profile

The fake profile needs **no Docker, no database, no credentials**: in-memory storage, a mock payment provider, a fake language model, a development identity provider (`tools/Gabinety.DevIdentity`). Everything is synthetic (`demo-` names) and lost on restart.

You need the .NET 10 SDK and pnpm (Node 22).

```bash
pnpm --dir web install --frozen-lockfile
dotnet run --project src/Gabinety.AppHost --launch-profile fake
```

The first run builds everything (about 1 to 2 minutes). The terminal prints only a spinner; use the checks below to see when it is ready. The AppHost also serves the Aspire dashboard on `http://localhost:15100` (logs of every service, the `seeder` resource); on a headless machine it is not needed.

| Part | URL |
|---|---|
| Web app (open this) | http://localhost:3000 |
| Dev identity (stand-in for the auth service) | http://localhost:5100 |
| Directory | http://localhost:5101 |
| Booking | http://localhost:5102 |
| Reputation | http://localhost:5103 |
| Assistant | http://localhost:5104 |

Each backend answers `GET /health` (the web app answers `/api/health`).

### Is it ready?

```bash
scripts/check-fake-profile.sh
```

It waits for every `/health`, then for six seeded practitioners, then for `/programisci` to answer 200. Printed on a stack that had finished seeding:

```
ok   devidentity http://localhost:5100/health
ok   directory http://localhost:5101/health
ok   booking http://localhost:5102/health
ok   reputation http://localhost:5103/health
ok   assistant http://localhost:5104/health
ok   web http://localhost:3000/api/health
ok   seeded practitioners: 6 (wanted 6)
ok   web /programisci
```

Exit code 0 means all ok; 1 means a line starts with `FAIL`. It waits up to `LIMIT` seconds (default 180) for each item.

### The seeder, and how to know it finished

Once Directory, Booking, Reputation and DevIdentity are healthy the AppHost starts the `seeder` resource by itself. It fills the system through the services' HTTP APIs: ten practitioners (six approved), four clients, availability, twelve bookings in every state, three reviews (`tools/Gabinety.DemoSeeder/README.md`). While it works it **moves the test clock of Booking** to make bookings complete, then puts it back to 0.

A seed is finished when **all three** hold: no `Gabinety.DemoSeeder` process is running, both clocks answer `offsetMinutes: 0` (section 5), and that stays true for about 10 seconds (the e2e harness uses exactly this test, `tests/e2e/harness/wait-for-profile.cjs`). `check-fake-profile.sh` shows the data is there but does not prove the seeder has stopped. Do not move the clock or book while it runs.

Measured: a full `--reset` (remove all `demo-` data, then seed again) against a running profile took **2 min 17 s**. Booking waits for its auto-complete job (it runs every 60 s) to complete three bookings, so expect roughly 2 minutes after the services are healthy, longer on a slow machine (the harness allows up to 8 minutes).

Run the seeder by hand (to top up, or to reset after you changed a lot):

```bash
K=demo-development-internal-key-not-a-secret
GABINETY_FAKE=1 Environment__Kind=Development \
  Backend__Directory=http://localhost:5101 Backend__Booking=http://localhost:5102 Backend__Reputation=http://localhost:5103 \
  Internal__Keys__Directory=$K Internal__Keys__Booking=$K Internal__Keys__Reputation=$K Identity__Authority=http://localhost:5100 \
  dotnet run --no-launch-profile --project tools/Gabinety.DemoSeeder -- --reset      # --reset-only removes; no argument tops up
```

Its last lines are `step reputation: done` with exit code 0. The AppHost dashboard has a `seeder-reset` resource that does the same (start it from the dashboard).

### Rate limit

Every caller (web server, seeder, services, browser) is one `localhost` address, and the services limit requests per address. The default is **60 a minute**; the AppHost sets `RateLimit__PerMinute=6000` **in the fake profile only**, so you will not meet a 429 there. If you start a service some other way you may.

## 2. Demo users

Sign in at http://localhost:3000/logowanie ("Zaloguj się"). One fixed **development** password for all seeded users (not a secret; it protects nothing and exists only in the stand-in identity provider):

| | |
|---|---|
| Password | `Demo-Dev-Passw0rd!` |

| User name | E-mail (what you type) | Role |
|---|---|---|
| `demo-client` | `demo-client@demo.example` | client |
| `demo-admin` | `demo-admin@demo.example` | administrator (`/admin/*`) |
| `demo-practitioner-1` to `demo-practitioner-8` | `demo-practitioner-N@demo.example` | practitioner accounts |

The six approved practitioners that appear in the public list, with the account behind each:

| Slug | Name | Account |
|---|---|---|
| `demo-anna-demowska` | Anna Demowska | `demo-practitioner-1` |
| `demo-bartosz-testowy` | Bartosz Testowy | `demo-practitioner-2` |
| `demo-celina-przykladowska` | Celina Przykładowska | `demo-practitioner-3` |
| `demo-dariusz-fikcyjny` | Dariusz Fikcyjny | `demo-practitioner-4` |
| `demo-ewa-wzorcowa` | Ewa Wzorcowa | `demo-practitioner-5` |
| `demo-filip-syntetyczny` | Filip Syntetyczny | `demo-practitioner-6` |

You can also register a new account at `/rejestracja`; any e-mail works (no verification step in the stand-in).

## 3. What to click: journeys J1 to J7

Each journey is an automated Playwright test in `tests/e2e/journeys/` (SPEC 10); the steps below are the same by hand. Polish button names are the app's own.

### J1 Browse a category and open a profile (no sign-in)
1. `/programisci`: the list of practitioners. In the "Filtry" navigation click a problem category: you land on `/programisci/<category>` and see "Wyniki: N".
2. Click a practitioner card: `/gabinet/<slug>` with "O mnie", "Oferta", ratings and "Zarezerwuj: <offer>" links.
3. Try it with JavaScript disabled in the browser: the page content is still in the HTML.

### J2 A new practitioner onboards, the administrator approves
1. `/rejestracja`: e-mail, password, tick "Akceptuję Regulamin...", "Załóż konto".
2. `/gabinet-programisty`: "Nazwa wyświetlana" then "Załóż gabinet": status "Szkic" (draft) and a checklist.
3. `/gabinet-programisty/profil`: fill "Nagłówek", "Opis" (200+ characters), "Link do portfolio", "NIP" (a checksum-valid synthetic one: `9900000000`), "Zapisz".
4. `/gabinet-programisty/oferty`: name, description, price in zł, "Rodzaj problemu", at least one technology, "Zapisz". (Availability: `/gabinet-programisty/dostepnosc`.)
5. Back on `/gabinet-programisty`: "Login GitHub", "Pokaż kod", "Sprawdź kod" (the fake accepts the check). All checklist items read "Gotowe".
6. "Wyślij do weryfikacji": "Czeka na weryfikację".
7. In another browser (or a private window) sign in as `demo-admin`, open `/admin/weryfikacja`, find the name, "Zatwierdź".
8. `/gabinet/<slug>` is now public and the profile is in `/programisci?q=<name>`.

### J3 Book, pay, complete, review (the main path)
1. Sign in as `demo-client`.
2. `/programisci`, open Celina Przykładowska (`/gabinet/demo-celina-przykladowska`; "Nowy" means no rating yet). Choose a time (a link like `10:30`) on an offer.
3. `/rezerwacja/<offer>`: "Opisz problem" (type a few sentences), "Zapłać".
4. `/checkout/mock/<bookingId>` ("Płatność (tryb demonstracyjny)"): "Symuluj udaną płatność". You land on `/rezerwacja/potwierdzenie/<bookingId>`: "Rezerwacja potwierdzona. Dziękujemy!". Note the booking id in the address.
5. `/moje/rezerwacje` shows it as "Potwierdzona". As `demo-practitioner-3`, `/moje/rezerwacje?role=practitioner` shows it with the brief.
6. Pass time (section 5): move the clock past the end of the consultation plus 48 hours (booking time + about 50 hours is enough). Booking's job runs every 60 s; within a minute the booking is completed.
7. `/moje/opinie/<bookingId>`: choose a rating, "Treść opinii", "Wyślij opinię": "Dziękujemy za opinię". The review is on the public profile. A practitioner shows "Nowy" until three reviews exist, then the average.

A profile that already has reviews (after running the e2e suite, or the seeded practitioners 1, 2 and 5) will not show "Nowy"; reseed (section 1) or use another practitioner.

### J4 The assistant is down
1. As `demo-client`, open a booking form (J3 steps 1 to 3) for `demo-dariusz-fikcyjny`.
2. In "Opisz problem" type a brief containing the text `[[unavailable]]` (the fake model answers "unavailable" to it), click "Pomóż mi opisać problem".
3. You see "Asystent jest chwilowo niedostępny. Opisz problem samodzielnie i zarezerwuj termin." and no suggestion; your text is untouched; "Zapłać" still books.

### J5 Cancel: refund early, none late
1. Book and pay (J3 steps 1 to 4) for a start time more than 24 hours ahead. Open `/moje/rezerwacje/<id>`, "Anuluj rezerwację": the dialog says 100 % refund; "Tak, anuluj rezerwację": "Zwrócona w całości".
2. Book another; move the clock to 12 hours before its start (section 5); cancel: the dialog says "brak zwrotu", the result is "Anulowana przez klienta (bez zwrotu)" and the payment stays "Opłacona".

### J6 Dispute and partial refund
1. As the client, book and pay; move the clock 30 minutes past the end.
2. `/moje/rezerwacje/<id>`: "Zgłoś spór", "Opis sporu", "Wyślij zgłoszenie": "Spór w toku".
3. Sign in as `demo-admin`, `/admin/spory`: find the booking id, "Rozstrzygnij", "Zwróć klientowi część", "Kwota zwrotu w zł", "Notatka", "Przejdź do potwierdzenia", "Potwierdź rozstrzygnięcie".
4. `/admin/ksiega`: the ledger (download as CSV). For a 250,00 zł booking refunded 100,00 zł: `ClientCharge` 25000, `Refund` 10000, `PlatformFee` 2250, `PractitionerPayout` 12750 (grosz). The payout row appears after Booking's payout job (every 60 s). `/admin/opinie` is the review-moderation list.

### J7 A foreign origin is refused
Automated only: a state-changing request through the web origin with a foreign `Origin` header, or `Sec-Fetch-Site: cross-site`, gets `403` with code `cross_site_request` and creates nothing. Run `pnpm --dir tests/e2e test -- --grep J7`.

## 4. Other pages

`/` home, `/jak-to-dziala`, `/cennik`, `/zasady-rankingu`, `/regulamin`, `/polityka-prywatnosci`, `/konto`, `/logowanie`, `/rejestracja`, `/moje/opinie` (a practitioner's reviews, "Opinie o mnie").

## 5. Advance the test clock

Booking and Reputation expose a clock offset (CONTRACTS 10.4), **fake profile only** (`/internal/v1/test/clock`; in a deployed environment it is a 404). It needs the internal key, in the fake profile the fixed value `demo-development-internal-key-not-a-secret` (AppHost `internal-key`; not a secret). Move both services together so they agree. Printed by these commands:

```bash
K=demo-development-internal-key-not-a-secret
curl -s -H "X-Internal-Key: $K" localhost:5102/internal/v1/test/clock
# {"offsetMinutes":0,"now":"2026-11-17T22:53:21.2475809+00:00"}

for p in 5102 5103; do
  curl -s -X POST -H "X-Internal-Key: $K" -H "Content-Type: application/json" -d '{"offsetMinutes":2940}' localhost:$p/internal/v1/test/clock
done
# {"offsetMinutes":2940,"now":"2026-11-19T23:53:21.2628268+00:00"}     (49 hours = 2940 minutes)
# {"offsetMinutes":2940,"now":"2026-11-19T23:53:21.2705997+00:00"}

for p in 5102 5103; do
  curl -s -X POST -H "X-Internal-Key: $K" -H "Content-Type: application/json" -d '{"offsetMinutes":0}' localhost:$p/internal/v1/test/clock
done
# {"offsetMinutes":0,"now":"2026-11-17T22:53:21.2769198+00:00"}
```

`offsetMinutes` is the absolute offset, not a step. Without the key the answer is `401`. The booking jobs (auto-complete, payout) run on the shifted time, once a minute: after moving the clock allow up to about 60 seconds. **Set it back to 0 when you are done**, and do not move it while the seeder runs.

## 6. Run the tests

| Layer | Command | Needs |
|---|---|---|
| .NET build and unit/contract tests | `dotnet build -warnaserror && dotnet test --filter "tier!=integration"` | .NET 10 SDK |
| .NET integration tier | `dotnet test --filter "tier=integration"` | Docker (Testcontainers starts PostgreSQL 17); CI also starts the real auth service image for the auth contract suite (`AUTH_BASE_URL`) |
| Web | `pnpm --dir web lint`, `typecheck`, `test`, `build` | pnpm, Node 22 |
| End to end | `pnpm --dir tests/e2e install --frozen-lockfile`, then `pnpm --dir tests/e2e exec playwright install chromium` once, then `pnpm --dir tests/e2e test` | a running fake profile (section 1) |
| Assistant evals (fake model) | part of the unit tier: `dotnet test tests/Gabinety.Assistant.Tests --filter "FullyQualifiedName~Evals"` | .NET |
| Assistant evals (live model) and live smoke | `EVAL_LIVE=1 dotnet test tests/Gabinety.Assistant.Tests --filter "tier=live"`, `scripts/smoke-llm.sh` | owner's machine, a real model, **spends credits** (see `evals/README.md`) |

Results measured:

- `dotnet build -warnaserror`: `Build succeeded. 0 Warning(s) 0 Error(s)`.
- `dotnet test --filter "tier!=integration"`: all projects passed (Llm 91, Architecture 43, Scheduling 10, Billing 36, Assistant 152 passed and 2 skipped, Ranking 14, Integration (unit tier) 123, Directory 355, Reputation 239, Booking 631), 1 min 10 s.
- `pnpm --dir web`: lint, typecheck, build exit 0; tests `Test Files 65 passed (65)`, `Tests 614 passed (614)`.
- `pnpm --dir tests/e2e test` against the fresh fake profile: `18 passed (2.9m)` (harness, J1 to J7).
- Evals with the fake model: `Passed! - Failed: 0, Passed: 19, Skipped: 2`.

End-to-end details:

- Chromium at a fixed path (sandboxes, some CI images): `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`. Traces off: `--trace off`. Pick journeys: `pnpm --dir tests/e2e test -- --grep "J3|J4"`.
- The run waits for the services and for the seeder to finish (section 1; up to `E2E_WAIT_SECONDS`, `E2E_SEEDER_WAIT_SECONDS`; `E2E_WAIT_FOR_SEEDER=0` skips it on a profile that has been seeded already).
- The journeys write data (J3 adds three reviews to Celina Przykładowska and then she no longer shows "Nowy"). CI throws its stack away. On a long-lived local profile run with `E2E_RESEED=1`: after each journey that wrote data the harness removes the `demo-` data and seeds again (minutes per run), so the next run starts clean. Without it a second full run can fail J3 ("start from the seeded profile with no reviews"); restart the profile or reseed (section 1) first.
- `scripts/smoke-llm.sh` makes ONE real model call. It refuses when `CI` is set or `Llm__Provider=Fake` (printed `smoke-llm: refusing to run when CI is set ...` and `smoke-llm: Llm:Provider is Fake; a smoke run needs a real model.`, exit 2).

## 7. The default profile and Docker

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile default
```

The default profile uses real components instead of fakes: PostgreSQL started by the AppHost in Docker (container `postgres`, volume `gabinety-pg`, host port 5432), and it asks for the internal key (`Parameters__internal-key`, or user secrets for the AppHost). The pinned auth service image is **not yet wired into the AppHost** (a comment in `src/Gabinety.AppHost/Program.cs` says it is added when T-01.01 records the image), and the web app and services point at `http://localhost:5105` for the identity authority, so sign-in does not work in this profile today. The seeder does not start by itself there (start `seeder` from the dashboard). This profile needs a Docker daemon; it was **not run** (section 8).

What Docker is for in this repository:

- **PostgreSQL** in the default profile (above), and in `deploy/fly/postgres.fly.toml` for staging (one instance, five databases from `deploy/fly/init-databases.sql`).
- **Testcontainers** in the integration tier of the tests (PostgreSQL 17); without a Docker daemon those tests fail with `DockerUnavailableException: Docker is either not running or misconfigured`.
- **Images**: `src/Gabinety.{Directory,Booking,Reputation,Assistant}/Dockerfile` and `web/Dockerfile`, checked in CI (`docker-build` jobs, `scripts/check-docker-image.sh <image>`: non-root, `/alive`, `Europe/Warsaw`). The real auth service is run from its pinned image in CI and deployed with `deploy/fly/authservice.fly.toml` (`docs/operations/AUTH.md`).
- **Deploy** configuration for Fly.io: `deploy/fly/*`.

Not needed for any of sections 1 to 6.

### What only the owner can do

Accounts and secrets are not something this repository can create (T-09.01): the Fly.io organisation and deploy token, the payment provider sandbox account and keys, the e-mail provider, a model provider key (or Copilot login) for the live smoke, the GitHub OAuth app for the auth service, the domain. Secrets are never committed and never pasted in a chat; the fake profile needs none.

## 8. What was not verified for this guide

- The default profile, the integration tier and the Docker images: the container used had no Docker daemon. `dotnet test --filter "tier=integration"` failed with `DockerUnavailableException` as expected; `dotnet run ... --launch-profile default` (with the internal key given) printed nothing and was still waiting after 100 s.
- `scripts/smoke-llm.sh` and the live evals against a real model (spend credits, need owner credentials). Only their refusals were run.
- Click-through by hand in a browser: the steps in section 3 are taken from the Playwright journeys, which pass (18 of 18), not from a person following this text.
