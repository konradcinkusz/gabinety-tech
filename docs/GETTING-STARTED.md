# Getting started

How to run Gabinety Tech on your machine, sign in, click through every journey (J1 to J7), pass business time, and run each test layer.

Every command below was run on Linux with the .NET SDK 10.0.404 (`global.json` asks for 10.0.100 or a later 10.0 feature band), Node 22 and pnpm 10, on 2026-11-17. Where a result is quoted it is what the command printed. Section 8 lists what could not be run in that environment.

## 1. Start the fake profile

The fake profile needs **no Docker, no database, no credentials**: in-memory storage, a mock payment provider, a fake language model, a development identity provider (`tools/Gabinety.DevIdentity`). Everything is synthetic (`demo-` names) and lost on restart.

You need the .NET 10 SDK (pnpm and Node 22 only for the end-to-end tests in `tests/e2e`). If `dotnet --version` is not found (a fresh container usually has no SDK), install it for your user, no root needed:

```bash
curl -sSL -o dotnet-install.sh https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"     # about 45 s
export PATH="$HOME/.dotnet:$PATH"; dotnet --version                       # 10.0.404 when this was written
```

(`ci.dot.net` is blocked in some sandboxes; `builds.dotnet.microsoft.com` is the host to use.) Then start the profile. `dotnet run` stays in the foreground for as long as the profile runs, so use a second terminal for everything below, or send it to the background (`nohup dotnet run ... > /tmp/apphost.log 2>&1 &`):

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile fake
```

The first run builds everything (about 1 to 2 minutes). The terminal prints only a spinner; use the checks below to see when it is ready. The AppHost also serves the Aspire dashboard on `http://localhost:15100` (logs of every service, the `seeder` resource); on a headless machine it is not needed.

| Part | URL |
|---|---|
| Web app, Blazor Web App (`src/Gabinety.Web`, ADR-0010; it replaced the Next.js app, see `docs/operations/PARITY.md`) | http://localhost:3000 |
| Dev identity (stand-in for the auth service) | http://localhost:5100 |
| Directory | http://localhost:5101 |
| Booking | http://localhost:5102 |
| Reputation | http://localhost:5103 |
| Assistant | http://localhost:5104 |

Each backend and the web host answer `GET /health` (the web host also `/alive`).

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
ok   web http://localhost:3000/health
ok   seeded practitioners: 6 (wanted 6)
ok   web /programisci
```

Exit code 0 means all ok; 1 means a line starts with `FAIL`. It waits up to `LIMIT` seconds (default 180) for each item.

### The seeder, and how to know it finished

Once Directory, Booking, Reputation and DevIdentity are healthy the AppHost starts the `seeder` resource by itself. It fills the system through the services' HTTP APIs: ten practitioners (six approved), four clients, availability, twelve bookings in every state, three reviews (`tools/Gabinety.DemoSeeder/README.md`). While it works it **moves the test clock of Booking** to make bookings complete, then puts it back to 0.

A seed is finished when **all three** hold: no `Gabinety.DemoSeeder` process is running, both clocks answer `offsetMinutes: 0` (section 5), and that stays true for about 10 seconds (the e2e harness uses exactly this test, `tests/e2e/harness/wait-for-profile.cjs`). `check-fake-profile.sh` shows the data is there but does not prove the seeder has stopped. Do not move the clock or book while it runs.

To wait for it in a script (the same test, polled every 5 s; run it after `scripts/check-fake-profile.sh` is green):

```bash
K=demo-development-internal-key-not-a-secret; stable=0
until [ $stable -ge 10 ]; do
  s=$(pgrep -f Gabinety.DemoSeeder | wc -l)
  a=$(curl -s -H "X-Internal-Key: $K" localhost:5102/internal/v1/test/clock | grep -o '"offsetMinutes":[0-9-]*')
  b=$(curl -s -H "X-Internal-Key: $K" localhost:5103/internal/v1/test/clock | grep -o '"offsetMinutes":[0-9-]*')
  if [ "$s" = 0 ] && [ "$a" = '"offsetMinutes":0' ] && [ "$b" = '"offsetMinutes":0' ]; then stable=$((stable+5)); else stable=0; fi; sleep 5
done; echo seeded
```

Measured on a cold start (see "Walkthrough record"): about 95 s from `dotnet run` until all services answered and the seeder started, and about 2 min 40 s more until the seeder had finished: roughly **4 min 20 s in all**. `check-fake-profile.sh` was already green while the seeder was still running and the Booking clock was shifted, so do not start a journey on that alone.

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

Sign in at http://localhost:3000/logowanie: "Zaloguj się". One fixed **development** password for all seeded users (not a secret; it protects nothing and exists only in the stand-in identity provider):

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
6. Pass time (section 5): move the clock past the end of the consultation plus 48 hours. The offset is counted **from now**, not from the booking: for a consultation that ends in 28 hours you need about 28 + 48 = 76 hours = 4560 minutes (add a margin). Booking's job runs every 60 s; within a minute the booking is completed (`canReview` turns true).
7. `/moje/opinie/<bookingId>`: choose a rating, "Treść opinii", "Wyślij opinię": "Dziękujemy za opinię". The review is on the public profile (in a browser; the review list is loaded by the page's JavaScript, so it is not in the HTML that `curl` gets, see J3 by curl below). A practitioner shows "Nowy" until three reviews exist, then the average. Set the clock back to 0 afterwards (section 5).

A profile that already has reviews (after running the e2e suite, or the seeded practitioners 1, 2 and 5) will not show "Nowy"; reseed (section 1) or use another practitioner.

#### J3 by curl (no browser)

The same path through the real HTTP surface, without a browser: the web host never lets a browser call a backend, so this talks to the services directly with the user's bearer token from the identity stand-in (what `tests/e2e/harness` does for its set-up). Needs `curl` and `jq`; run it after the seeder has finished (section 1). The web host's own pages (step 7) need a session cookie, which only the sign-in form sets, so they are checked here anonymously.

```bash
W=localhost:3000; B=localhost:5102/api/v1; R=localhost:5103/api/v1
K=demo-development-internal-key-not-a-secret
tok() { curl -s -H 'Content-Type: application/json' -d "{\"email\":\"$1@demo.example\",\"password\":\"Demo-Dev-Passw0rd!\"}" localhost:5100/api/v1/auth/login | jq -r .accessToken; }   # never print it
CT=$(tok demo-client); PT=$(tok demo-practitioner-3)
auth() { echo "Authorization: Bearer $1"; }

# 1. browse (no sign-in): the public profile read straight from Directory has the ids
curl -s localhost:5101/api/v1/practitioners/demo-celina-przykladowska | jq -c '{id,isNew,offer:.offerings[0].id}'
P=<id from above>; OFF=<offer id>
curl -s -H "$(auth $CT)" "$B/practitioners/$P/slots?offeringId=$OFF&from=$(date +%F)&to=$(date -d '+13 days' +%F)" | jq -c '.slots[0]'
START=<startUtc of a slot>; END=<its endUtc>

# 2. book: 201 {"bookingId":...,"status":"PendingPayment","checkoutUrl":...}
ID=$(curl -s -H "$(auth $CT)" -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" -H 'Content-Type: application/json' \
  -d "{\"offeringId\":\"$OFF\",\"startUtc\":\"$START\",\"brief\":{\"text\":\"Prototyp z modelem językowym.\",\"aiAssisted\":false}}" $B/bookings | jq -r .bookingId)

# 3. pay on the mock: {"outcome":"Applied"}; the booking is then Confirmed / payment Succeeded
curl -s -H "$(auth $CT)" -H 'Content-Type: application/json' -d "{\"bookingId\":\"$ID\",\"outcome\":\"succeeded\"}" $B/payments/simulate
curl -s -H "$(auth $CT)" $B/bookings/$ID | jq -c '{status,payment}'
curl -s -H "$(auth $PT)" $B/bookings/$ID | jq -c '{status,brief:.brief.text}'   # the practitioner sees it, with the brief

# 4. pass time: minutes from NOW to END + 48 h (+ a margin), on both services
M=$(( ( $(date -d "$END" +%s) + 48*3600 - $(date +%s) ) / 60 + 60 ))
for p in 5102 5103; do curl -s -X POST -H "X-Internal-Key: $K" -H 'Content-Type: application/json' -d "{\"offsetMinutes\":$M}" localhost:$p/internal/v1/test/clock; done

# 5. wait for the auto-complete job (took 21 s when run; allow up to about 70 s)
until curl -s -H "$(auth $CT)" $B/bookings/$ID | jq -e '.status=="Completed" and .canReview' >/dev/null; do sleep 5; done

# 6. review: 201 {"reviewId":...,"status":"Published"}
curl -s -H "$(auth $CT)" -H 'Content-Type: application/json' -d "{\"bookingId\":\"$ID\",\"rating\":5,\"text\":\"Rzetelna konsultacja.\"}" $R/reviews
for p in 5102 5103; do curl -s -X POST -H "X-Internal-Key: $K" -H 'Content-Type: application/json' -d '{"offsetMinutes":0}' localhost:$p/internal/v1/test/clock; done

# 7. the published review (public, no cookie), and the pages
curl -s $R/practitioners/$P/reviews | jq -c '.items[0]'
for u in /programisci /gabinet/demo-celina-przykladowska; do curl -s -o /dev/null -w "$u %{http_code}\n" $W$u; done
curl -s -o /dev/null -w "/moje/rezerwacje %{http_code}\n" $W/moje/rezerwacje      # 302 to /logowanie without the session cookie
```

`GET $B/me/bookings` needs `?role=client` or `?role=practitioner` (400 without it).

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
Automated only: the sign-in POST to the web origin with a foreign `Origin` header, or `Sec-Fetch-Site: cross-site`, gets `403` with code `cross_site_request` and creates nothing. Run `pnpm --dir tests/e2e test -- --grep J7`.

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

`offsetMinutes` is the absolute offset, not a step. Without the key the answer is `401`. **Offsets are from now.** `offsetMinutes` moves "now"; it does not jump to a booking. To complete a booking that ends at `END`, use (`END` + 48 h − the real now) in minutes. The 2940 minutes above (49 h) only complete a booking that ended a few minutes ago. A first try with 4500 minutes against a consultation ending 28 h ahead left it `Confirmed` (75 h is less than 28 + 48 h plus the time already passed); 5000 completed it within 21 s. The booking jobs (auto-complete, payout) run on the shifted time, once a minute: after moving the clock allow up to about 60 seconds. **Set it back to 0 when you are done**, and do not move it while the seeder runs.

## 6. Run the tests

| Layer | Command | Needs |
|---|---|---|
| .NET build and unit/contract tests | `dotnet build -warnaserror && dotnet test --filter "tier!=integration"` | .NET 10 SDK |
| .NET integration tier | `dotnet test --filter "tier=integration"` | Docker (Testcontainers starts PostgreSQL 17); CI also starts the real auth service image for the auth contract suite (`AUTH_BASE_URL`) |
| Web host | part of the unit tier: `dotnet test tests/Gabinety.Web.Tests` (host, component and render-mode tests, no network) | .NET 10 SDK |
| End to end | `pnpm --dir tests/e2e install --frozen-lockfile`, then `pnpm --dir tests/e2e exec playwright install chromium` once, then `pnpm --dir tests/e2e test` (the web host on :3000; `E2E_WEB_URL` overrides the address) | a running fake profile (section 1) |
| Memory per Blazor circuit | `ONLY_CIRCUITS=1 scripts/measure-performance.sh` (publishes a Release host, opens N interactive pages, reads the process memory; `docs/operations/PERFORMANCE.md`) | the fake profile, `pnpm --dir tests/e2e install`, Chromium |
| Assistant evals (fake model) | part of the unit tier: `dotnet test tests/Gabinety.Assistant.Tests --filter "FullyQualifiedName~Evals"` | .NET |
| Assistant evals (live model) and live smoke | `EVAL_LIVE=1 dotnet test tests/Gabinety.Assistant.Tests --filter "tier=live"`, `scripts/smoke-llm.sh` | owner's machine, a real model, **spends credits** (see `evals/README.md`) |

Results measured:

- `dotnet build -warnaserror`: `Build succeeded. 0 Warning(s) 0 Error(s)`.
- `dotnet test --filter "tier!=integration"`: all projects passed (Llm 91, Architecture 43, Scheduling 10, Billing 36, Assistant 152 passed and 2 skipped, Ranking 14, Integration (unit tier) 123, Directory 355, Reputation 239, Booking 631), 1 min 10 s.
- `pnpm --dir tests/e2e test` against the fresh fake profile: `18 passed (2.9m)` (harness, J1 to J7).
- Evals with the fake model: `Passed! - Failed: 0, Passed: 19, Skipped: 2`.

End-to-end details:

- Chromium at a fixed path (sandboxes, some CI images): `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`. Traces off: `--trace off`. Pick journeys: `pnpm --dir tests/e2e test -- --grep "J3|J4"`.
- Interactive pages are entered with `gotoInteractive`/`watchCircuit` (`tests/e2e/harness/circuit.ts`), which wait for the circuit (`StartCircuit` answered on the websocket) before anything is typed; nothing sleeps. The web host has no `/api/proxy`, so the set-up and verification calls of the journeys go to the backends directly with the signed-in user's token (`harness/auth.ts`).
- The run waits for the services and for the seeder to finish (section 1; up to `E2E_WAIT_SECONDS`, `E2E_SEEDER_WAIT_SECONDS`; `E2E_WAIT_FOR_SEEDER=0` skips it on a profile that has been seeded already).
- The journeys write data (J3 adds three reviews to Celina Przykładowska and then she no longer shows "Nowy"). CI throws its stack away. On a long-lived local profile run with `E2E_RESEED=1`: after each journey that wrote data the harness removes the `demo-` data and seeds again (minutes per run), so the next run starts clean. Without it a second full run can fail J3 ("start from the seeded profile with no reviews"); restart the profile or reseed (section 1) first.
- `scripts/smoke-llm.sh` makes ONE real model call. It refuses when `CI` is set or `Llm__Provider=Fake` (printed `smoke-llm: refusing to run when CI is set ...` and `smoke-llm: Llm:Provider is Fake; a smoke run needs a real model.`, exit 2).

## 7. The default profile and Docker

```bash
dotnet run --project src/Gabinety.AppHost --launch-profile default
```

The default profile uses real components instead of fakes. The AppHost starts, in Docker: PostgreSQL (container `postgres`, volume `gabinety-pg`, host port 5432, five databases `authdb`, `directorydb`, `bookingdb`, `reputationdb`, `assistantdb` as in `deploy/fly/init-databases.sql`) and the **real authservice** (`ghcr.io/konradcinkusz/authservice`, v0.3.4, pinned by digest, the same one as `deploy/fly/authservice.fly.toml`) on http://localhost:5105. The four services and the web app take `Identity__Authority=http://localhost:5105`, `Identity__Audience=gabinety` and `Backend__Auth=http://localhost:5105`; each service gets its own database through its `ConnectionStrings__<db>`. It needs a **Docker daemon** and asks for the internal key once (`Parameters__internal-key`, or the AppHost's user secrets: `dotnet user-secrets set Parameters:internal-key "<anything>" --project src/Gabinety.AppHost`).

| What | Value in the default profile |
|---|---|
| Signing key | RS256, generated in memory on every start (a new key per start is harmless: sign in again). To keep one, set the parameter `Parameters:authservice-jwt-key` (a PKCS#8 PEM, made outside the work tree with `scripts/gen-authservice-key.sh`) |
| PostgreSQL password | fixed development value `demo-development-postgres-not-a-secret` (the data volume keeps the password of its first start), override with `Parameters__postgres-password` before the first start |
| E-mail confirmation | off (`Auth__RequireConfirmedEmail=false`): registering answers `200` with a session, never `202`; there is no mail sender |
| First administrator | `demo-admin@demo.example` / `Demo-Dev-Passw0rd!` (`InitialAdmin__*`; change the address with `InitialAdmin:Email`). authservice gives this account the platform role **`SuperAdmin`**, not `Admin`; the services and the web treat both as administrator. `/admin` works with it |
| GitHub sign-in | off (needs a real OAuth app); the "Zaloguj się przez GitHub" button leads to an error page. See `docs/operations/AUTH.md` section 5 |
| Rate limit | authservice answers `429` after 20 sign-in or registration calls a minute from one address; the web forwards the visitor's address (`Fly-Client-IP`), on `localhost` that is you alone |
| Passwords | authservice demands 8 or more characters with a digit, a lower-case and an upper-case letter and a special character (the web form says so; the stand-in of the fake profile enforces the same) |

Differences from the fake profile you will notice: user ids (`sub`) are opaque GUIDs, not `demo-...`, so the demo reset of Booking and Reputation (CONTRACTS 10.4, which keys on a `demo-` user id) does not recognise those users' rows; the seeder (`seeder` in the dashboard, started by hand) logs in or registers the demo users through authservice's public endpoints and works, but it was **not run** against this profile. The practitioners' language model (`Llm__Provider`) is the real one and needs credentials: the Assistant is not waited for by anything.

What was and was not proven (`docs/operations/AUTH.md` section 9): the CI `integration` job runs the real authservice image against everything the web needs and against the services' own admin policies; **no run of this AppHost profile itself exists**, because it needs Docker.

What Docker is for in this repository:

- **PostgreSQL** in the default profile (above), and in `deploy/fly/postgres.fly.toml` for staging (one instance, five databases from `deploy/fly/init-databases.sql`).
- **Testcontainers** in the integration tier of the tests (PostgreSQL 17); without a Docker daemon those tests fail with `DockerUnavailableException: Docker is either not running or misconfigured`.
- **Images**: `src/Gabinety.{Directory,Booking,Reputation,Assistant}/Dockerfile` and `src/Gabinety.Web/Dockerfile`, checked in CI (`docker-build` jobs, `scripts/check-docker-image.sh <image>`: non-root, `/alive`, `Europe/Warsaw`). The real auth service is run from its pinned image in CI and deployed with `deploy/fly/authservice.fly.toml` (`docs/operations/AUTH.md`).
- **Deploy** configuration for Fly.io: `deploy/fly/*`.

Not needed for any of sections 1 to 6.

### What only the owner can do

Accounts and secrets are not something this repository can create (T-09.01): the Fly.io organisation and deploy token, the payment provider sandbox account and keys, the e-mail provider, a model provider key (or Copilot login) for the live smoke, the GitHub OAuth app for the auth service, the domain. Secrets are never committed and never pasted in a chat; the fake profile needs none.

## 8. What was not verified for this guide

- The default profile (including the authservice container added to it in the real-authservice change; it compiles and its image digest is checked by `scripts/check-authservice-pin.sh`, but nobody has started it), the integration tier and the Docker images: the container used had no Docker daemon. `dotnet test --filter "tier=integration"` failed with `DockerUnavailableException` as expected; `dotnet run ... --launch-profile default` (with the internal key given) printed nothing and was still waiting after 100 s.
- `scripts/smoke-llm.sh` and the live evals against a real model (spend credits, need owner credentials). Only their refusals were run.
- Click-through by hand in a browser: the steps in section 3 are taken from the Playwright journeys, which pass (18 of 18), not from a person following this text. The J3 path was later followed on a fresh checkout with `curl` only (see "Walkthrough record"); the clicking itself, the Polish button names and the page rendering in a real browser (including the client-loaded review list on the public profile) were still not done by a person.

## Walkthrough record

*A historical record of 2026-11-18, when the web app was still the Next.js one (`web/`, retired by the cutover of 2026-11-22): the commands and URLs below are what ran then, not what to type now.*

Cold-start check of this guide (T-08.06), by a fresh session that followed the guide and read no other source until it had to (the curl calls were then taken from `tests/e2e/harness`, see the first finding).

| | |
|---|---|
| Date | 2026-11-18 (container clock 07:45 to 07:58 UTC) |
| Container | Linux 6.18, 4 cores, 15 GB, Node 22.23.5, pnpm 10.28.0, no .NET SDK at the start, no Docker, no browser used |
| .NET | none installed; installed 10.0.404 with `dotnet-install.sh --channel 10.0` from `builds.dotnet.microsoft.com` (47 s) |
| Total | about 13 min wall clock, of which the start-up and seeding took 4 min 18 s and the booking path (book, pay, clock, complete, review) about 90 s |

What ran and what printed:

1. `pnpm --dir web install --frozen-lockfile`: `Done in 3s`.
2. `dotnet run --project src/Gabinety.AppHost --launch-profile fake` (in the background): printed only NuGet restore lines and a spinner, as the guide says.
3. `scripts/check-fake-profile.sh`, first run at 99 s: eight `ok` lines (all five `/health`, web, `seeded practitioners: 6 (wanted 6)`, `/programisci`), exit 0, **while the seeder was still running** (a `Gabinety.DemoSeeder` process existed and the Booking clock read `offsetMinutes: 5894`).
4. The three-part seed-finished test: true at 258 s after launch. **Seeding took about 2 min 40 s** (it started before 95 s and ended at 258 s, including the 10 s of stability).
5. `/`, `/programisci`, `/gabinet/demo-celina-przykladowska`, `/logowanie`: 200 (the profile has "O mnie", "Nowy", and "Zarezerwuj: Pierwszy prototyp z modelem językowym"). `/moje/rezerwacje` without a cookie: 307 to `/logowanie?redirect=%2Fmoje%2Frezerwacje`.
6. Sign in as `demo-client` and `demo-practitioner-3` through `/api/auth/login`: 200. Slots read (first free slot the next day 11:00 UTC). Booking: 201 `PendingPayment` with a `checkoutUrl`. `payments/simulate`: `{"outcome":"Applied"}`; the booking `Confirmed`, payment `Succeeded`; the practitioner read it with the brief.
7. Clock: first 4500 minutes (too little: still `Confirmed`), then 5000: `Completed`, `canReview: true` 21 s later. Review: 201 `Published`. Clock back to 0 on both services. The review list of the practitioner (public, no cookie) shows it. `/moje/rezerwacje` (client) 200 with "Zakończona"; as the practitioner with `?role=practitioner` 200; `/moje/opinie/<id>`, `/checkout/mock/<id>`, `/rezerwacja/potwierdzenie/<id>` 200. `isNew` stays true with one review, as the guide says.

8. After the fixes the "J3 by curl" script was extracted from this file and run unchanged (only the three `<...>` placeholders filled by lookups): every step printed what the comments say, the whole run took 11 s (the offset worked out to 4620 minutes, the booking completed within the first poll), a second review was `Published`. The profile therefore ended with two reviews and still `isNew: true`.

Steps that needed fixing (all in this file):

- **No way to get the .NET SDK** was given although the guide says "You need the .NET 10 SDK" and a fresh container has none: install snippet added to section 1, with the host that works when `ci.dot.net` is blocked.
- **`dotnet run` blocks the terminal** and the guide did not say to use another terminal or the background: added.
- **No curl recipe**: the guide describes only clicks (and the acceptance criterion asks for the HTTP surface). The ids, the sign-in (cookie jar through `/api/auth/login`), the proxy prefixes (`/api/proxy/booking`, `/api/proxy/reputation`), the idempotency key, the body shapes and `me/bookings?role=` had to be found in `tests/e2e/harness`. Added "J3 by curl".
- **Clock offset example misleading**: "booking time + about 50 hours" and the 49 h example suggest the offset is relative to the booking. It is relative to now; my first offset (4500 min) was too small. Explained, with a formula, in J3 step 6, section 5 and the curl recipe.
- **Seeding wait**: the guide gave the rule but no command and no measured cold-start time; added both. `check-fake-profile.sh` green does not mean seeding is over (already said, now measured).
- **"The review is on the public profile"** cannot be checked with `curl` on the page (the list is rendered by the browser); after 12 minutes the HTML still read "Nowy na platformie: ... nie ma jeszcze opinii". The review is read from the public API instead; a note was added.

Not done: a real-browser click-through. It needs a person on a machine with a browser (it cannot be done from a cloud container); the Playwright journeys J1 to J7 cover the UI in CI.

Also not done in this walkthrough: any real browser session (clicking, the Polish labels, the review list on the profile as a person sees it); J1, J2, J4 to J7; cancelling, disputes, the administrator pages; posting the review after the clock was already back at 0 (it was posted with the clock still shifted, so its `createdAt` shows the shifted date); the default profile, Docker and the tests of section 6 (not asked for).
