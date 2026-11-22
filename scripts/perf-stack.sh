#!/usr/bin/env bash
# Starts or stops the stack that scripts/measure-performance.sh measures (T-10.04): DevIdentity, the four services (Release builds) and the web app
# (a Release publish of src/Gabinety.Web, run as Production: scripts/perf/blazor-host.sh), wired exactly like the AppHost fake profile (same environment: mock payments, fake model, RateLimit__PerMinute=6000), but
#   PROFILE=postgres  the services use PostgreSQL (SPEC 13 says "on PostgreSQL"); needs a server on PGHOST:PGPORT with the databases of deploy/fly/init-databases.sql
#   PROFILE=inmemory  the services use the in-memory provider (what the AppHost fake profile does)
# The AppHost is not used because its fake profile forces InMemory and its default profile needs Docker. The web app is a Release publish because the AppHost
# runs a Debug build, whose timings say nothing about the deployed app. Run the script from another checkout (a git worktree) to measure another commit.
#   scripts/perf-stack.sh start|stop|status
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
STATE="${PERF_DIR:-${TMPDIR:-/tmp}/gabinety-perf}"
PROFILE="${PROFILE:-postgres}"
WEB_PORT="${WEB_PORT:-3000}"
START_BACKENDS="${START_BACKENDS:-1}"   # 0: only the web app (the backends are already running)
START_WEB="${START_WEB:-1}"
# The AppHost fake profile uses 6000 a minute, which scales the public-read limit to 12000 a minute (200 a second) per address (RateLimits.cs): 20 clients on loopback exceed that
# in a second and the run would measure the limiter. The measurement raises it; the seeder (which makes thousands of calls) is fine with either.
RATE_PER_MINUTE="${RATE_PER_MINUTE:-1000000}"
SERVICES="${SERVICES:-devidentity directory booking reputation assistant}"   # SERVICES="directory booking reputation assistant" restarts the services but keeps the identity stand-in (its users live in memory)
PGHOST="${PGHOST:-127.0.0.1}"; PGPORT="${PGPORT:-5432}"; PGUSER="${PGUSER:-postgres}"
KEY=demo-development-internal-key-not-a-secret   # the fixed, documented development value of the fake profile; protects nothing
mkdir -p "$STATE"

common_env() {
  export Environment__Kind=Development Internal__Key="$KEY" GABINETY_FAKE=1 RateLimit__PerMinute="$RATE_PER_MINUTE"
  export Backend__Directory=http://localhost:5101 Backend__Booking=http://localhost:5102 Backend__Reputation=http://localhost:5103 Backend__Assistant=http://localhost:5104
  export Internal__Keys__Directory="$KEY" Internal__Keys__Booking="$KEY" Internal__Keys__Reputation="$KEY" Internal__Keys__Assistant="$KEY"
  export Llm__Provider=Fake Payments__Provider=Mock Notifications__Provider=Log Identity__Authority=http://localhost:5100
  if [ "$PROFILE" = postgres ]; then
    export Database__Provider=Postgres
    for db in directorydb bookingdb reputationdb assistantdb; do export "ConnectionStrings__$db=Host=$PGHOST;Port=$PGPORT;Database=$db;Username=$PGUSER;Maximum Pool Size=50"; done
  else
    export Database__Provider=InMemory
  fi
}

launch() { # name, project, port
  ( common_env; ASPNETCORE_URLS="http://localhost:$3" nohup dotnet run -c Release --no-launch-profile --project "$2" > "$STATE/$1.log" 2>&1 & echo $! > "$STATE/$1.pid" )
}

wait_url() { for ((i = 0; i < ${2:-180}; i++)); do [ "$(curl -s -o /dev/null -w '%{http_code}' -m 3 "$1" || true)" = 200 ] && return 0; sleep 1; done; echo "FAIL not healthy: $1" >&2; return 1; }

case "${1:-}" in
  start)
    if [ "$START_BACKENDS" = 1 ]; then
      if [ "$PROFILE" = postgres ]; then
        for db in directorydb bookingdb reputationdb assistantdb; do
          psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -tAc "select 1 from pg_database where datname='$db'" | grep -q 1 || psql -h "$PGHOST" -p "$PGPORT" -U "$PGUSER" -qc "create database $db"
        done
      fi
      for proj in src/Gabinety.Directory src/Gabinety.Booking src/Gabinety.Reputation src/Gabinety.Assistant tools/Gabinety.DevIdentity; do dotnet build -c Release "$ROOT/$proj" -v q --nologo > "$STATE/build.log" 2>&1 || { cat "$STATE/build.log"; exit 1; }; done
      for svc in $SERVICES; do
        case $svc in
          devidentity) launch devidentity "$ROOT/tools/Gabinety.DevIdentity" 5100 ;;
          directory) launch directory "$ROOT/src/Gabinety.Directory" 5101 ;;
          booking) launch booking "$ROOT/src/Gabinety.Booking" 5102 ;;
          reputation) launch reputation "$ROOT/src/Gabinety.Reputation" 5103 ;;
          assistant) launch assistant "$ROOT/src/Gabinety.Assistant" 5104 ;;
        esac
      done
      for p in 5100 5101 5102 5103 5104; do wait_url "http://localhost:$p/health"; done
      echo "backends up, PROFILE=$PROFILE"
    fi
    if [ "$START_WEB" = 1 ]; then
      BLAZOR_PORT="$WEB_PORT" PERF_DIR="$STATE" "$ROOT/scripts/perf/blazor-host.sh" start >&2
      wait_url "http://localhost:$WEB_PORT/alive"
      echo "web up on $WEB_PORT (Release publish of src/Gabinety.Web)"
    fi ;;
  stop)
    for f in "$STATE"/*.pid; do [ -e "$f" ] || continue; pkill -P "$(cat "$f")" 2>/dev/null || true; kill "$(cat "$f")" 2>/dev/null || true; rm -f "$f"; done
    pkill -f "Gabinety\.(Directory|Booking|Reputation|Assistant|DevIdentity)" 2>/dev/null || true; pkill -f "Gabinety\.Web\.dll" 2>/dev/null || true ;;
  status)
    for p in 5100 5101 5102 5103 5104; do echo "$p $(curl -s -o /dev/null -w '%{http_code}' -m 2 "http://localhost:$p/health" || true)"; done
    echo "web $WEB_PORT $(curl -s -o /dev/null -w '%{http_code}' -m 2 "http://localhost:$WEB_PORT/alive" || true)" ;;
  *) echo "usage: scripts/perf-stack.sh start|stop|status" >&2; exit 2 ;;
esac
