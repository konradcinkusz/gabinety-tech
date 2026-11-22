# Performance measurement (T-10.04)

> **After the cutover (2026-11-22):** the web app is the Blazor host. Sections 1 to 7 are the measurement of **2026-11-18, when the web app was the Next.js one** (`next start`, one Node process); they stay as the record of that run and of the commit it measured, and the script now measures the Blazor host (a Release publish, `scripts/perf/blazor-host.sh`), so a re-run gives new numbers for the web rows. The backend rows are unaffected. Section 8 (Blazor circuits) is the current web-host capacity measurement.

SPEC section 13 rows, measured on one container on 2026-11-18, with the command, the environment and the number for each. **These numbers are not staging numbers.** Staging does not exist yet (owner accounts pending), the load generator shared four cores with the system it measured, and the dataset is synthetic. A target row that says "pass" here says only that this container did not miss it; A-15 (SPEC section 12.1, "the targets are reachable on the sizes of section 4.3") stays **not shown** until the staging rows are measured.

Raw results: `docs/operations/performance/2026-11-18/` (`main/results.json` is the run quoted below; `main/commands.txt` has every command in full). Script: `scripts/measure-performance.sh` (stack: `scripts/perf-stack.sh`, load: `scripts/perf/load.mjs`).

## 1. Environment

| | |
|---|---|
| Date / time | 2026-11-18, run started 10:56 UTC |
| Commit measured | `e272acf` (branch `task/T-10.04-performance`; the code under test is `main` at `face8a7`, the branch changes only the seeder, tests and scripts) |
| Machine | this cloud container: Intel Xeon @ 2.10 GHz, **4 cores, 15.7 GiB RAM**, Linux 6.18.44 |
| Versions | .NET SDK 10.0.404, Node v22.23.5, pnpm 10.28, PostgreSQL **16.15** (staging is `postgres:17-alpine`, `deploy/fly/postgres.fly.toml`), Lighthouse 13.6.0 (via `npx`, pinned), Chromium 153 (`/opt/pw-browsers`) |
| Profile | **The services ran on PostgreSQL, not the in-memory fake profile.** DevIdentity, the fake language model, the mock payment provider and the log notifier are the fake profile's (`Llm__Provider=Fake`, `Payments__Provider=Mock`, `Notifications__Provider=Log`, `GABINETY_FAKE=1`); `Database__Provider=Postgres` with one local server holding `directorydb`, `bookingdb`, `reputationdb`, `assistantdb`. Release builds of the services. |
| Why not the AppHost | its fake profile forces `Database__Provider=InMemory`, and its default profile needs a Docker daemon (none here). `scripts/perf-stack.sh` starts the same processes with the same environment as the AppHost fake profile, except the database, the web app and the rate limit (next two rows). |
| Web app | **production build, `next start`** (the AppHost runs `next dev`, whose timings say nothing about the deployed app). One Node process. |
| Rate limit | `RateLimit__PerMinute=1000000`, **not the AppHost's 6000**. At 6000 the public-read limit scales to 12000 a minute (200 a second) per address (`RateLimits.cs`), and 20 clients on loopback exceed that within a second, so the run would measure the limiter. Every run asserts that all requests answered 2xx. |
| Load generator | `scripts/perf/load.mjs`, on the same machine, closed loop, keep-alive, 20 clients, 3 s warm-up not counted, 20 s counted. Why it is not autocannon (no p95: it reports p90 and p97.5) or hey (`go install` was refused in this session, so it could not be installed): it records every request and computes exact nearest-rank percentiles. |
| Not comparable with staging | one machine for load, web, four services and the database; loopback network; PostgreSQL 16 on a local disk with `fsync=off`; a warm JIT after the seeding; no TLS; no CDN. |

## 2. The dataset

Seeder with `--factor 200` (`tools/Gabinety.DemoSeeder/README.md`, "Scaled dataset"): factor N makes 10 x N practitioners and 100 x N reviews in all.

| | Count (read from PostgreSQL after the seed) |
|---|---|
| Practitioners | **2000** (1996 Approved, 1 Submitted, 1 Draft, 1 Suspended, 1 Rejected) |
| Reviews | **20000 as rating counts on the Directory** (`sum(rating_count)` = 19996 on the Approved + 4 on the Suspended as measured; since the catalog was made honest, ADR-0012 item 3, the catalog carries only the 3 reviews the seeder creates and the scale extras carry the rest, all on Approved practitioners). **No review rows were written to Reputation** except the three of the ordinary demo: a review needs a Completed booking and the seeder cannot make thousands. A row that reads reviews from Reputation itself (the profile's review list) is therefore not measured at this size. |
| Offerings | 2998 |
| Bookings | 12 of the ordinary demo, plus those the measurement itself created |
| Seeding time | about 4 min (the whole seeder run including the ordinary demo, 12935 changes in the scale step); log: `performance/2026-11-18/seed-factor-200.log` |

Booking availability exists only for the six catalog practitioners (the slots and booking rows use them; the number of other practitioners does not change a slots query).

## 3. Results (SPEC section 13)

| Row | Tool and command | Measured here | Target | Result |
|---|---|---|---|---|
| `GET /api/v1/practitioners` filtered list, 12 items, p95, Directory on PostgreSQL, 20 clients | `scripts/perf/load.mjs --concurrency 20 --duration 20` over 19 filtered queries (problem x duration, problem x tech), all returning 12 items; 24103 requests, 1204.6 req/s, all 200 | **p95 29.8 ms** (p50 15.1, p99 37.4). Client-side total on loopback, so at or above the server time | <= 300 ms | pass (on this machine) |
| `GET .../slots` 14 days, p95, Booking on PostgreSQL | same tool, 20 clients over the 10 slots URLs of the six practitioners with availability; 17912 requests, 894.9 req/s, all 200 | **p95 37.8 ms** (p50 20.7, p99 48.2) | <= 200 ms | pass (on this machine) |
| `POST /api/v1/bookings`, p95, excluding the provider call | same tool, 120 requests, 4 clients, a distinct non-overlapping slot and Idempotency-Key each; all 201; the mock payment provider answers in process | **p95 19.4 ms** (p50 11.4, p99 23.0), 0.38 s for the 120. **Caveat:** the first booking run on a freshly started Booking process (a smoke run of the same script, 20 requests, cold) measured **p95 428.6 ms**; the recorded run is warm. 120 requests in 0.4 s is a short sample | <= 500 ms | pass warm (cold: 428.6 ms, also under the target) |
| Category page time to first byte, cached, p75, web on staging from outside | same tool on `/programisci/audyt-kodu` of the production build, TTFB = request start to response headers, one warm-up request first | **1 client: p75 7.7 ms** (p50 6.2, p95 9.8); **20 clients: p75 143.5 ms** (p50 126.9, p95 171.6; 150 req/s: the single Node process is saturated, so this is queueing). The verdict takes the worse | <= 800 ms | pass **locally only**. **Not measured: from outside, on staging** (not deployed) |
| Category page LCP, mobile profile, lab, Lighthouse in CI against staging | `npx --yes lighthouse@13.6.0 <url> --only-categories=performance --chrome-flags='--headless --no-sandbox'`, three runs, default mobile emulation (Moto G Power, simulated slow 4G, 4x CPU slowdown), local production build | **LCP 911.6 ms** (median; runs 911.6, 910.1, 913.6), CLS 0, FCP 911.6 (LCP = FCP), TBT 58 to 108 ms, performance score 0.99 to 1.00 | <= 2500 ms | pass **locally only**. **Not measured: against staging, and there is no Lighthouse job in CI** (see tasks) |
| `brief` end to end, p95 | - | **not measured** | <= 20 s | needs the Assistant on a real bring-your-own-key model (T-06.05, owner's key); the fake model proves nothing about it |
| Job lag (hold expiry, auto-complete, payout), p99, staging, 24 h | - | **not measured** | <= 120 s | staging not deployed; the row asks for 24 hours on staging |

Nothing is rounded: values are the nearest-rank percentile of every request, shown to 0.1 ms.

### What happened during the measurement (read before trusting the numbers)

- A first full run (`first-run-database-died/`) was **invalid after its first three rows**: the PostgreSQL server of this container crashed (the sandbox tightened the permissions of the scratch directory that held its data, `could not open file ... pg_control: Permission denied`) while cached pages kept answering 200. Its list, slots and booking rows had finished before the crash and agree with the recorded run (p95 28.7, 36.4 and 20.1 ms); its web rows are not used. The database was moved to `/var/lib/gabinety-pg`, the run repeated in full, and the script now fails the run (`INVALID RUN`) if the stack is not healthy at its end.
- The warm-up request of the category page fills Next's data cache (`revalidate = 300`), so "cached" here means the Directory fetch is served from that cache. The page itself is rendered per request (section 4).

## 4. Effect of PR #113 (CSP nonce, `export const dynamic = "force-dynamic"` in the root layout)

Method: a git worktree of the parent of the #113 merge commit (`1a12bc1`, `a4536c4^`), built and started with `next start` on port 3001, against the same backends and data as the current web app (port 3000). Three pages, 1 client and 20 clients, 20 s each, one warm-up request first. Done three times: in the main run (baseline first), once more with the order reversed, and once in the first run (baseline first; not used, see above). Raw: `main/results.json` and `pr113-reverse-order/results.json`.

**What the build output says (the finding that matters).** Before #113 only three pages were static (`○`): `/regulamin`, `/jak-to-dziala`, `/polityka-prywatnosci`. `/`, `/programisci`, `/programisci/[problem]`, `/programisci/[problem]/[tech]`, `/gabinet/[slug]`, `/cennik` and `/zasady-rankingu` were **already dynamic (`ƒ`) before #113**; the response headers agree (`Cache-Control: private, no-cache, no-store` on them before and after, `s-maxage=31536000` and `x-nextjs-cache: HIT` only on `/regulamin`). After #113 every page is `ƒ`. So the profile, category and home pages were not turned from ISR into per-request rendering by #113: they were per-request already. (Guess, not checked: the list and category pages read `searchParams`, which makes a route dynamic; why `/gabinet/[slug]` was dynamic too is not known.) What #113 changed is the three static pages.

TTFB p50 / p95 in ms and requests per second, before -> after (main run; reversed order in brackets):

| Page | 1 client | 20 clients |
|---|---|---|
| `/programisci` | 6.11 / 10.3, 144 rps -> 7.13 / 10.9, 134 rps (5.52 / 9.9, 155 -> 6.15 / 10.7, 141) | 130 / 182, 146 rps -> 131 / 175, 148 rps (114 / 159, 165 -> 125 / 168, 153) |
| `/gabinet/demo-filip-syntetyczny` | 3.63 / 6.06, 238 rps -> 3.88 / 6.72, 221 rps (3.61 / 6.05, 241 -> 3.79 / 6.2, 230) | 73 / 107, 256 rps -> 74 / 109, 250 rps (71 / 101, 262 -> 77 / 109, 243) |
| `/regulamin` | **0.91 / 1.34, 973 rps -> 3.10 / 5.43, 280 rps** (0.84 / 1.27, 1044 -> 2.97 / 5.25, 288) | **16.1 / 25.6, 1142 rps -> 56.3 / 84.2, 330 rps** (14.7 / 25.6, 1218 -> 55.3 / 83.7, 332) |

- `/regulamin` (and by the same mechanism `/jak-to-dziala` and `/polityka-prywatnosci`, not measured separately): about 3.4x slower TTFB unloaded and 3.5x under load, and about a third of the throughput, in all runs. In absolute terms 3 ms unloaded and 56 ms at 20 clients: far from the 800 ms of the category row, but it is the one real regression, and it is the price of the nonce.
- `/programisci` and `/gabinet/<slug>`: between 1 and 17 percent slower at the median (in both orders) and mostly 2 to 9 percent lower throughput (one case, 20 clients on `/programisci`, 1 percent higher); the differences are the size of the run-to-run spread (the first, invalid run showed 38 percent on `/programisci`, with the database down; it is not used). **Not a conclusion either way**; the per-request nonce and CSP in `proxy.ts` is a plausible small cost (guess).
- Not measured: Lighthouse on the baseline (LCP is unaffected in kind: the HTML is the same size).

## 5. Not shown (what was not measured and why)

- Every row "on staging" or "from outside": staging is not deployed (owner accounts pending). The local numbers above are lab numbers on one machine.
- `brief` end to end: needs a real BYOK model and the owner's key (T-06.05).
- Job lag, p99 over 24 hours: needs staging for 24 hours; the local Booking jobs were not timed. (The jobs run every 60 s by design, `docs/GETTING-STARTED.md`, so their lag floor is what the schedule says, not what a load test shows.)
- Lighthouse in CI: no such job exists.
- 20000 review **rows**: only rating counts were seeded (section 2); reads of Reputation's review table at that size are not covered.
- The web app from a second machine, TLS, a CDN, a cold start of any service, PostgreSQL 17, a Fly shared CPU: none of it.
- Concurrency for slots and bookings is not stated by SPEC 13; 20 and 4 are my choices.
- The category-page "cached" TTFB ignores a cache miss: the first request after a restart or after 300 s goes to the Directory.

## 6. Tasks to create

None of the measured rows missed its target on this machine, so none of these is a miss; they are what the measurement found.

1. **Measure the staging rows once staging exists** (T-10.04 stays open for them): list, slots, booking p95 on the staging stack at the scaled factor; category TTFB p75 from outside; job lag p99 over 24 h; `brief` p95 with the owner's key. Run `scripts/measure-performance.sh` against the staging URLs (it needs the base URLs as parameters; today it assumes localhost: a small change).
2. **A Lighthouse job in CI against staging** (SPEC 13 names it; none exists). Local result to compare with: LCP 911.6 ms, CLS 0.
3. **Make the three legal pages static again, or accept the cost.** #113 made `/regulamin`, `/jak-to-dziala`, `/polityka-prywatnosci` per-request: 3.4x TTFB, a third of the throughput, and they lose the shared cache (`s-maxage=31536000`). Guess: a hash-based CSP for pages without dynamic content, or serving them through a route group the proxy does not touch, would restore it; the security trade-off is the supervisor's and the owner's call.
4. **Decide whether the category and profile pages should be ISR at all.** SPEC 13 says "cached"; they were dynamic before #113 and still are, so the cache is only Next's data cache for the Directory fetch, and every page view is rendered by one Node process that saturates at about 150 requests a second (20 clients: p75 143 ms). Guess at the cause: `searchParams` on the list and category pages; unknown for the profile.
5. **Booking after a restart is slower**: a cold first booking run had p95 428.6 ms (n = 20) against 19.4 ms warm. Under the target, but on staging every deploy is a cold start. Guess: JIT and first-use of the EF model and the payment mock. Measure on staging after a deploy.
6. **Seeding review rows at scale** (only if the reviews row should be measured on Reputation itself): a Reputation demo endpoint that inserts reviews directly would be needed; the seeder cannot make thousands of Completed bookings.

## 7. Reproduce

```bash
export PATH=$HOME/.dotnet:$PATH
# 1. a PostgreSQL server on 127.0.0.1:5432 (any; the databases are created by the script):  PGHOST PGPORT PGUSER override the defaults
PROFILE=postgres scripts/perf-stack.sh start          # builds and starts DevIdentity, four services (Release), and the web app (a Release publish of the Blazor host)
# 2. the scaled dataset
K=demo-development-internal-key-not-a-secret
GABINETY_FAKE=1 Environment__Kind=Development Backend__Directory=http://localhost:5101 Backend__Booking=http://localhost:5102 Backend__Reputation=http://localhost:5103 \
  Internal__Keys__Directory=$K Internal__Keys__Booking=$K Internal__Keys__Reputation=$K Identity__Authority=http://localhost:5100 \
  dotnet run -c Release --no-launch-profile --project tools/Gabinety.DemoSeeder -- --factor 200          # about 4 min
# 3. restart the four services (not DevIdentity) so the measurement starts from fresh processes (optional):  SERVICES="directory booking reputation assistant" scripts/perf-stack.sh start
# 4. measure (the PR #113 comparison was of two Next.js builds and no longer applies; BASELINE_WEB_URL may still name any second web app)
scripts/measure-performance.sh       # about 10 minutes; prints the table, writes perf-results/<date>/results.json
```

The AppHost's own fake profile can be measured too (`dotnet run --project src/Gabinety.AppHost --launch-profile fake`), but its rate limit is 6000 a minute (the read rows will hit 429 and the script reports them as `invalid`), it is in-memory, and its web app is a Debug build of the Blazor host: not the numbers of this file.

## 8. Blazor circuits: memory per open interactive page (ADR-0010)

**Measured in a CI-like container (4 cores, 15.7 GiB, Linux 6.18), not on Fly.** `ONLY_CIRCUITS=1 scripts/measure-performance.sh` publishes a **Release** build of `src/Gabinety.Web`, runs it as Production with the circuit limits of `deploy/fly/web-blazor.fly.toml` and a GC heap cap of 75 % of 512 MB (how the runtime reads a cgroup limit; there is no container here), signs in N times through the form (N sessions), opens `/moje/rezerwacje` (an interactive page) in N Chromium pages, waits for each circuit, lets RSS settle, and reads `/proc/<pid>`. The pages are idle after load: a user typing into the booking form or using the assistant costs more than this. Date 2026-11-22, services on the in-memory fake profile.

| Open circuits | RSS (MB) | RssAnon (MB) | Pss (MB) | MB per circuit (vs 0) | Marginal MB per added circuit |
|---|---|---|---|---|---|
| 0 (idle, warmed) | 143.2 | 28.0 | 68.2 | - | - |
| 10 | 173.8 | 49.2 | 97.7 | 3.1 | 3.1 |
| 25 | 206.0 | 77.0 | 129.9 | 2.5 | 2.1 |
| 50 | 217.9 | 86.3 | 141.8 | 1.5 | 0.5 |
| 100 | 245.3 | 112.0 | 169.1 | 1.0 | 0.5 |
| 150 | 269.4 | 136.0 | 193.2 | 0.8 | 0.5 |

**Conclusion for the 512 MB machine.** 150 idle interactive pages fit at 269 MB RSS, under the 384 MB left after 25 % headroom. The cost is not linear (the GC grows the heap in steps): about 3 MB per circuit for the first ten, about 0.5 MB for each further one. Extrapolating the first-level slope gives a conservative 78 pages and the late slope about 500; only the 150 measured are proven. Plan on roughly 100 to 150 simultaneous active users per machine, and set the proxy limits (soft 100, hard 150 connections) there. About 115 MB of the RSS is shared file-backed runtime pages (RSS minus RssAnon); the anonymous memory is 136 MB at 150 circuits.

**Not shown:** Fly itself (shared CPU, its memory accounting, the proxy), a Debug build (the AppHost's own Blazor resource: do not measure that), circuits doing work (typing, the assistant, live pages), disconnected-circuit retention churn, GC pauses and CPU under load, more than 150 circuits, real backends with data at scale.
