#!/usr/bin/env bash
# Starts or stops a RELEASE build of the Blazor host (src/Gabinety.Web) against the services that are already running (the fake profile or scripts/perf-stack.sh), for the circuit
# memory measurement (scripts/perf/measure-circuits.mjs). The AppHost's own Blazor resource is a Debug build; its memory says nothing about the deployed image, so this publishes
# Release (the same `dotnet publish` the Dockerfile runs) and runs it as Production with the settings of deploy/fly/web-blazor.fly.toml that matter for memory.
#   scripts/perf/blazor-host.sh start|stop|pid      env: BLAZOR_PORT (3002)  PERF_DIR  BACKENDS_HOST (localhost)  MEMORY_LIMIT_MB (512: caps the GC like the Fly machine's cgroup would)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
STATE="${PERF_DIR:-${TMPDIR:-/tmp}/gabinety-perf}"; mkdir -p "$STATE"
PORT="${BLAZOR_PORT:-3002}"; H="${BACKENDS_HOST:-localhost}"; LIMIT="${MEMORY_LIMIT_MB:-512}"
case "${1:-}" in
  start)
    dotnet publish "$ROOT/src/Gabinety.Web/Gabinety.Web.csproj" -c Release -o "$STATE/blazor-publish" /p:UseAppHost=false -v:q -nologo >&2
    # No container here, so the 512 MB machine is emulated the way the runtime reads a cgroup limit: GCHeapHardLimit of 75 % (the runtime's default share inside a container).
    export ASPNETCORE_URLS="http://127.0.0.1:$PORT" ASPNETCORE_ENVIRONMENT=Production TZ=Europe/Warsaw DOTNET_gcServer=0
    export DOTNET_GCHeapHardLimit="$(printf '0x%x' $((LIMIT * 1024 * 1024 * 75 / 100)))"
    export Site__BaseUrl="http://localhost:$PORT" Backend__Directory="http://$H:5101" Backend__Booking="http://$H:5102" Backend__Reputation="http://$H:5103" Backend__Assistant="http://$H:5104" Backend__Auth="http://$H:5100"
    export Identity__Issuer="${Identity__Issuer:-}" Circuit__DisconnectedMaxRetained=20 Circuit__DisconnectedRetentionSeconds=120 Circuit__MaxBufferedRenderBatches=5 Circuit__MaxReceiveBytes=32768
    export DataProtection__KeysPath="$STATE/blazor-keys"
    # the content root is the working directory (as in the image, WORKDIR /app): wwwroot is found next to the dll only from there
    cd "$STATE/blazor-publish"
    nohup dotnet Gabinety.Web.dll > "$STATE/blazor.log" 2>&1 &
    echo $! > "$STATE/blazor.pid"
    for _ in $(seq 1 60); do [ "$(curl -s -o /dev/null -w '%{http_code}' -m 2 "http://127.0.0.1:$PORT/alive" || true)" = 200 ] && { echo "blazor host (release) pid $(cat "$STATE/blazor.pid") on :$PORT" >&2; exit 0; }; sleep 1; done
    echo "FAIL: the Blazor host did not answer /alive; see $STATE/blazor.log" >&2; exit 1;;
  stop) [ -f "$STATE/blazor.pid" ] && kill "$(cat "$STATE/blazor.pid")" 2>/dev/null || true; rm -f "$STATE/blazor.pid";;
  pid) cat "$STATE/blazor.pid";;
  *) echo "usage: blazor-host.sh start|stop|pid" >&2; exit 2;;
esac
