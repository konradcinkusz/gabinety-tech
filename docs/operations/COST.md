# COST: monthly estimate and the P7 diagnostic (T-09.02)

Staging, Fly.io, region `fra`, one machine per app (`--ha=false`), sizes of SPEC 4.3 as written in `deploy/fly/*.fly.toml`.

## 1. Prices: UNVERIFIED

Written 2026-11-18. The Fly price page (`https://fly.io/docs/about/pricing/`) could **not** be reached from the session that wrote this file (its network refuses `fly.io`).
The rates below are therefore **assumptions recalled from Fly's public list, not read today**. Every figure in section 2 is `assumed rate x count`; replace the rates
and the totals follow. The owner (or T-10.04 at M2) re-checks them against the price page before the first deploy.

| Assumed rate (USD, UNVERIFIED) | Value |
|---|---|
| `shared-cpu-1x`, 256 MB, running all month | 2.02 |
| `shared-cpu-1x`, 512 MB, running all month | 3.32 |
| `shared-cpu-1x`, 1024 MB, running all month | 5.92 |
| Volume, per GB-month | 0.15 |
| Shared IPv4 and private (Flycast) IPv6 | 0 (assumed free; a **dedicated** IPv4 is assumed 2.00 and is not used) |
| A stopped machine | 0 for CPU/RAM (only its volume, if any, is billed); the Assistant has no volume |
| Bandwidth, backups storage (T-10.05), registry | not estimated (staging traffic is negligible; backups are T-10.05's) |

## 2. Estimate per app

| App | Size | Machines | Arithmetic | USD / month |
|---|---|---|---|---|
| `gabinety-web-blazor-staging` (the web host, a stateful frontend: section 4) | 512 MB | 1, always on | 3.32 | 3.32 |
| `gabinety-directory-staging` | 512 MB | 1, always on | 3.32 | 3.32 |
| `gabinety-booking-staging` | 512 MB | 1, always on | 3.32 | 3.32 |
| `gabinety-reputation-staging` | 256 MB | 1, always on | 2.02 | 2.02 |
| `gabinety-authservice-staging` | 512 MB | 1, always on | 3.32 | 3.32 |
| `gabinety-postgres-staging` | 1 GB + 5 GB volume | 1, always on | 5.92 + 5 x 0.15 | 6.67 |
| `gabinety-assistant-staging` | 1024 MB | `min_machines_running = 0` | ceiling: 5.92 (running all month) | 5.92 |
| **Total (ceiling)** | | | 3.32 x 4 + 2.02 + 6.67 + 5.92 | **27.89** |

The Assistant is counted at its ceiling. If it runs 10 % of the month (an **assumption**, not a measurement) it costs 0.59 and the total is 22.56. If the three public apps
were given dedicated IPv4 addresses the total rises by 3 x 2.00 = 6.00 (not planned: `*.fly.dev` shared addresses are used until a domain exists).

**Stated total: 27.89 USD per month (unverified rates, ceiling).** Sizes are SPEC 4.3 starting points; A-15 (the performance targets fit them) is measured in T-10.04, and this table is re-checked at M2.

## 3. The P7 diagnostic: every in-request call has a callee that pins a machine

Rule (`fly-io-deployment` section 7, `reference-architecture` P7): for every synchronous call A to B, either B keeps a machine running, or A's timeout comfortably
exceeds B's cold start. Read from the HttpClients (`src/Gabinety.*/Clients`, `Jobs/*/Directory*Client.cs`), `src/Gabinety.Web/Api` and SPEC 5.1:

| Caller to callee | Call | Callee pins a machine? | Address in `fly.toml` |
|---|---|---|---|
| every service, web to authservice | JWKS fetch (and re-fetch on cache expiry), login, refresh | yes: authservice `min_machines_running = 1`, `auto_stop_machines = "off"` | public URL (it is the issuer) |
| all services to PostgreSQL | every request and job | yes: one always-on machine, no HTTP service to stop | `.internal:5432` |
| web to Directory | reads, writes | yes: min 1, auto stop off | `.internal:8080` |
| web to Booking | slots, `POST /bookings` | yes: min 1 | `.internal:8080` |
| web to Reputation | reads | yes: min 1 | `.internal:8080` |
| Booking to Directory | `GET /internal/v1/offerings/{id}` (in-request), stats `PUT` (job) | yes: Directory min 1 | `.internal:8080` |
| Reputation to Booking | reviewability | yes: Booking min 1 | `.internal:8080` |
| Reputation to Directory | reputation `PUT` (job) | yes | `.internal:8080` |
| Assistant to Directory | taxonomy | yes (and the cached value is used on failure) | `.internal:8080` |
| payment provider to Booking | webhook | yes: Booking min 1 (the proxy would also start it) | public |
| **web to Assistant** | `brief`, 35 s | **no: min 0.** Second branch of the rule: web's 35 s timeout covers the cold start plus the model's 30 s (SPEC 5.1) and the feature degrades to the plain form (P8) | `.flycast` (goes through the proxy, which starts the machine; `.internal` would not) |
| **Reputation to Assistant** | triage, 25 s | **no: min 0.** Second branch, but the 25 s must cover cold start plus a 25 s model budget: **unmeasured**. A timeout only means the review is published without the model (CONTRACTS 5.3), so it degrades, not fails | `.flycast` |

Result: every in-request callee pins a machine except the Assistant, which SPEC 4.3 allows to stop (optional, P8). The two calls into it are the only ones that rely on the
timeout branch; both have a working path without it. **Open item for T-09.07/T-10.04:** measure the Assistant cold start (1 GB VM plus the Copilot runtime); if it exceeds
roughly 10 s, set the Assistant to `min_machines_running = 1` (about +5.92 USD/month, a cost decision for the owner).

Two mechanics the table depends on (not shown against a real Fly account): `.flycast` needs `fly ips allocate-v6 --private --app gabinety-assistant-staging` once, and the
private apps are deployed with `--no-public-ips`. Without the private IPv6 the `.flycast` name does not resolve and both calls degrade.

## 4. The Blazor web app (ADR-0010): no scale to zero, memory budget

`gabinety-web-blazor-staging` (`deploy/fly/web-blazor.fly.toml`) replaced the Next.js app `gabinety-web-staging` at the cutover. It is a stateful frontend: `min_machines_running = 1`, auto stop off, one machine, so **it costs the always-on figure and can never be idle-stopped** (a stop ends every open page). Size 512 MB shared-cpu-1x: 3.32 USD a month at the assumed rates of section 1, the same as the Next app was. Add a 1 GB volume for the Data Protection keys: 1 x 0.15 = **0.15 USD**. The estate ceiling of section 2 therefore rises by 0.15 (27.89 to 28.04), and **by another 3.32 for the fortnight or so while the old Next.js app stays as the rollback** (`CUTOVER.md` section 3).

Memory budget (measured in a CI-like container, **not on Fly**, `PERFORMANCE.md` section 8): idle host 143 MB RSS, 269 MB with 150 idle interactive pages, 512 MB machine. About 100 to 150 simultaneous active pages per machine; the proxy sheds above 150 connections. If real use exceeds that, 1 GB is 5.92 USD (+2.60 a month); a second machine needs session affinity and a shared session store first, not just a count.
