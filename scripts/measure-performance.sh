#!/usr/bin/env bash
# Measures every row of SPEC 13 that can be measured on this machine, prints the table and writes JSON (T-10.04).
#
# Prerequisites: a running, SEEDED system. Start it with scripts/perf-stack.sh and seed it at factor 200 (2000 practitioners, 20000 reviews, SPEC 13):
#   PROFILE=postgres scripts/perf-stack.sh start
#   <seed>   (see docs/operations/PERFORMANCE.md, "Reproduce": the seeder with --factor 200)
#   scripts/measure-performance.sh
# Environment:
#   OUT                results directory (default ./perf-results/<UTC date>)          LH_RUNS   Lighthouse runs (default 3)
#   WEB_URL            the web app measured (default http://localhost:3000)           DURATION  seconds per load run (default 20)
#   BASELINE_WEB_URL   a second web app built from the commit before PR #113, for the comparison of section "PR #113" (optional)
#   DB_PROFILE         label written to the report: postgres | inmemory                BOOKINGS  POST /bookings requests (default 120)
#   CHROME_PATH        Chromium for Lighthouse (default /opt/pw-browsers/chromium-*/chrome-linux64/chrome)
#   SKIP_LIGHTHOUSE=1  skip the Lighthouse rows                                  ONLY_PR113=1  run only the PR #113 comparison (needs BASELINE_WEB_URL)
#   BLAZOR circuits (ADR-0010, "memory per circuit"): ONLY_CIRCUITS=1 measures only the Blazor host's memory under N open interactive pages and exits (needs the fake profile or perf-stack running, and
#                      `pnpm --dir tests/e2e install`); without ONLY_CIRCUITS the section runs after the others when BLAZOR_CIRCUITS=1.
#                      BLAZOR_START=1 (default) publishes and starts a RELEASE build on BLAZOR_PORT (3002), capped like the 512 MB Fly machine (scripts/perf/blazor-host.sh); BLAZOR_URL + BLAZOR_PID measure a host
#                      you started yourself. CIRCUIT_LEVELS "10,25,50,100" (cumulative), CIRCUIT_PAGE (/moje/rezerwacje: an interactive page), MEMORY_LIMIT_MB (512).
#   PR113_SIDES        order of the two web apps in the comparison (default "baseline current"; run it again as "current baseline" to see an order effect)
# Tools: scripts/perf/load.mjs (HTTP load, exact percentiles; the reason it is not autocannon or hey is in its header) and Lighthouse, pinned below, fetched by npx
# on demand (nothing is added to any package.json). Exit code: 0 when the measurement ran (a miss is a result, not a failure), 1 when it could not run.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${OUT:-$PWD/perf-results/$(date -u +%Y-%m-%d)}"
WEB_URL="${WEB_URL:-http://localhost:3000}"
DURATION="${DURATION:-20}"; BOOKINGS="${BOOKINGS:-120}"; LH_RUNS="${LH_RUNS:-3}"; CONC="${CONC:-20}"
DB_PROFILE="${DB_PROFILE:-postgres}"
LIGHTHOUSE_VERSION=13.6.0   # pinned; npx --yes lighthouse@$LIGHTHOUSE_VERSION
LOAD="node $ROOT/scripts/perf/load.mjs"
mkdir -p "$OUT/raw"; : > "$OUT/commands.txt"

say() { printf '%s\n' "$*" >&2; }
run() { # id, command...  : records the command, runs it, keeps stdout as raw/<id>.json
  local id="$1"; shift
  printf '%s\t%s\n' "$id" "$*" >> "$OUT/commands.txt"
  say "== $id"
  "$@" > "$OUT/raw/$id.json"
}

# ---- Blazor circuits: memory of the Blazor host per open interactive page (a function, so it can run alone or after the other rows) ----
circuits() {
  local url="${BLAZOR_URL:-}" pid="${BLAZOR_PID:-}" started=0
  if [ -z "$url" ]; then
    BLAZOR_PORT="${BLAZOR_PORT:-3002}" MEMORY_LIMIT_MB="${MEMORY_LIMIT_MB:-512}" "$ROOT/scripts/perf/blazor-host.sh" start >&2 || return 1
    url="http://localhost:${BLAZOR_PORT:-3002}"; pid="$("$ROOT/scripts/perf/blazor-host.sh" pid)"; started=1
  fi
  printf 'blazor-circuits\t%s\n' "node scripts/perf/measure-circuits.mjs --url $url --pid $pid --levels ${CIRCUIT_LEVELS:-10,25,50,100}" >> "$OUT/commands.txt"
  say "== blazor-circuits"
  local rc=0
  node "$ROOT/scripts/perf/measure-circuits.mjs" --url "$url" --pid "$pid" --levels "${CIRCUIT_LEVELS:-10,25,50,100}" --page "${CIRCUIT_PAGE:-/moje/rezerwacje}" \
    --budget-mb "${MEMORY_LIMIT_MB:-512}" --out "$OUT" >&2 || rc=$?
  [ "$started" = 1 ] && "$ROOT/scripts/perf/blazor-host.sh" stop
  return $rc
}
if [ "${ONLY_CIRCUITS:-0}" = 1 ]; then
  for u in http://localhost:5100 http://localhost:5101 http://localhost:5102; do
    [ "$(curl -s -o /dev/null -w '%{http_code}' -m 3 "$u/health" || true)" = 200 ] || { say "FAIL: $u/health is not healthy; start the fake profile or scripts/perf-stack.sh"; exit 1; }
  done
  circuits || exit 1
  say "wrote $OUT/blazor-circuits.json and blazor-circuits.md"; exit 0
fi

for u in http://localhost:5100 http://localhost:5101 http://localhost:5102 "$WEB_URL"; do
  path=/health
  [ "$(curl -s -o /dev/null -w '%{http_code}' -m 3 "$u$path" || true)" = 200 ] || { say "FAIL: $u$path is not healthy; start the stack with scripts/perf-stack.sh"; exit 1; }
done
approved=$(curl -s "http://localhost:5101/api/v1/sitemap" | jq '.practitioners | length')
say "approved practitioners in the Directory: $approved"

# ---- environment ----
{
  echo "{"
  echo "\"date\": \"$(date -u +%Y-%m-%dT%H:%M:%SZ)\","
  echo "\"commit\": \"$(git -C "$ROOT" rev-parse HEAD)\","
  echo "\"cpu\": \"$(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2- | sed 's/^ //')\", \"cores\": $(nproc), \"memoryGiB\": $(awk '/MemTotal/ {printf "%.1f", $2/1048576}' /proc/meminfo),"
  echo "\"kernel\": \"$(uname -sr)\", \"dotnet\": \"$(dotnet --version 2>/dev/null || echo n/a)\", \"node\": \"$(node --version)\","
  echo "\"postgres\": \"$(psql -h "${PGHOST:-127.0.0.1}" -U "${PGUSER:-postgres}" -tAc 'show server_version' 2>/dev/null || echo n/a)\","
  echo "\"dbProfile\": \"$DB_PROFILE\", \"approvedPractitioners\": $approved, \"webUrl\": \"$WEB_URL\", \"webMode\": \"Blazor host, Release publish, Production\","
  echo "\"durationSeconds\": $DURATION, \"concurrency\": $CONC, \"lighthouse\": \"$LIGHTHOUSE_VERSION\""
  echo "}"
} > "$OUT/environment.json"

# ---- inputs ----
node "$ROOT/scripts/perf/prepare.mjs" "$OUT/inputs" "$BOOKINGS" >&2
LISTS=$(jq -r 'join(",")' "$OUT/inputs/list-urls.json"); SLOTS=$(jq -r 'join(",")' "$OUT/inputs/slots-urls.json")
CATEGORY=$(jq -r .web.category "$OUT/inputs/prep.json"); PROFILE_URL=$(jq -r .web.profile "$OUT/inputs/prep.json"); TERMS=$(jq -r .web.terms "$OUT/inputs/prep.json"); LIST_PAGE=$(jq -r .web.list "$OUT/inputs/prep.json")

# ---- SPEC 13 rows that load the services ----
if [ "${ONLY_PR113:-0}" != 1 ]; then
run list      $LOAD --name list --concurrency "$CONC" --duration "$DURATION" --warmup 3 --urls "$LISTS"
run slots     $LOAD --name slots --concurrency "$CONC" --duration "$DURATION" --warmup 3 --urls "$SLOTS"
run bookings  $LOAD --name bookings --concurrency 4 --requests "$OUT/inputs/booking-requests.json"

# ---- the category page: time to first byte, cached (a first request fills the cache; it is not counted), unloaded and under load ----
curl -s -o /dev/null "$CATEGORY"
run category-c1   $LOAD --name category-c1 --concurrency 1 --duration "$DURATION" --warmup 3 --urls "$CATEGORY"
run category-c20  $LOAD --name category-c20 --concurrency "$CONC" --duration "$DURATION" --warmup 3 --urls "$CATEGORY"

# ---- Lighthouse, mobile profile (its default: Moto G Power emulation, simulated slow 4G, 4x CPU slowdown), lab, against the local web app ----
if [ "${SKIP_LIGHTHOUSE:-0}" != 1 ]; then
  CHROME_PATH="${CHROME_PATH:-$(ls -d /opt/pw-browsers/chromium-*/chrome-linux64/chrome 2>/dev/null | head -1)}"; export CHROME_PATH
  for i in $(seq 1 "$LH_RUNS"); do
    printf 'lighthouse-%s\t%s\n' "$i" "npx --yes lighthouse@$LIGHTHOUSE_VERSION $CATEGORY --quiet --only-categories=performance --output=json --chrome-flags='--headless --no-sandbox'" >> "$OUT/commands.txt"
    say "== lighthouse-$i"
    npx --yes "lighthouse@$LIGHTHOUSE_VERSION" "$CATEGORY" --quiet --only-categories=performance --output=json --output-path="$OUT/raw/lighthouse-$i.full.json" \
      --chrome-flags="--headless --no-sandbox" >&2
    jq '{lcpMs: .audits["largest-contentful-paint"].numericValue, cls: .audits["cumulative-layout-shift"].numericValue, fcpMs: .audits["first-contentful-paint"].numericValue,
         tbtMs: .audits["total-blocking-time"].numericValue, ttfbMs: .audits["server-response-time"].numericValue, score: .categories.performance.score, version: .lighthouseVersion,
         throttling: .configSettings.throttlingMethod, formFactor: .configSettings.formFactor, finalUrl: .finalDisplayedUrl}' "$OUT/raw/lighthouse-$i.full.json" > "$OUT/raw/lighthouse-$i.json"
  done
fi
fi

# ---- PR #113: the same three pages on the web app of the commit before it (BASELINE_WEB_URL) and on this one ----
if [ -n "${BASELINE_WEB_URL:-}" ]; then
  slug=$(basename "$PROFILE_URL")
  for side in ${PR113_SIDES:-baseline current}; do
    base="$BASELINE_WEB_URL"; [ $side = current ] && base="$WEB_URL"
    for page in programisci:/programisci gabinet:/gabinet/$slug regulamin:/regulamin; do
      name=${page%%:*}; path=${page#*:}
      curl -s -o /dev/null "$base$path"; curl -s -o /dev/null "$base$path"
      for c in 1 "$CONC"; do run "pr113-$side-$name-c$c" $LOAD --name "pr113-$side-$name-c$c" --concurrency "$c" --duration "$DURATION" --warmup 3 --urls "$base$path"; done
    done
  done
fi

# ---- Blazor circuits (opt-in here: it opens up to 100 browser pages) ----
if [ "${BLAZOR_CIRCUITS:-0}" = 1 ]; then circuits || say "Blazor circuit measurement failed (the other rows stand)"; fi

# the run is valid only if the stack stayed up to the end (a database that died mid-run once left cached pages answering 200 for a while)
for u in http://localhost:5101/api/v1/sitemap http://localhost:5102/health "$WEB_URL/health"; do
  [ "$(curl -s -o /dev/null -w '%{http_code}' -m 5 "$u" || true)" = 200 ] || { say "INVALID RUN: $u does not answer 200 after the measurement"; exit 1; }
done
node "$ROOT/scripts/perf/report.mjs" "$OUT"
