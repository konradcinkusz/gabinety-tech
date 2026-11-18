#!/usr/bin/env bash
# T-09.03: post-deploy smoke of one app, with retry. Usage: smoke.sh <app-key> <fly-app> <mode> <path>      mode as in deploy/estate.conf
# Env: SMOKE_RETRIES (12) SMOKE_DELAY seconds (10)  PUBLIC_URL (default https://<fly-app>.fly.dev, mode public)  FLYCTL (default flyctl)
# Exit 0 = healthy (or, for ssh-optional, no machine is running by design). The last line is "SMOKE <key> <ok|skipped|FAIL> <detail>".
set -uo pipefail
key="${1:?usage: smoke.sh <app-key> <fly-app> <mode> <path>}"; app="${2:?}"; mode="${3:?}"; path="${4:--}"
retries="${SMOKE_RETRIES:-12}"; delay="${SMOKE_DELAY:-10}"; fly="${FLYCTL:-flyctl}"
base="${PUBLIC_URL:-https://$app.fly.dev}"

probe() {
  case "$mode" in
    public)
      curl -fsS -o /dev/null -m 10 "${base%/}$path" ;;
    ssh|ssh-optional)
      # The backend images have bash but no curl or wget (the same trick as their HEALTHCHECK).
      "$fly" ssh console --app "$app" --command "bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf \"GET $path HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n\" >&3 && head -n1 <&3 | grep -q \" 200 \"'" ;;
    ssh-pg)
      "$fly" ssh console --app "$app" --command "pg_isready -h 127.0.0.1 -p 5432" ;;
    *) echo "unknown smoke mode $mode" >&2; return 2 ;;
  esac
}

if [ "$mode" = ssh-optional ]; then
  running="$("$fly" machines list --app "$app" --json 2>/dev/null | jq '[.[] | select(.state == "started")] | length' 2>/dev/null || echo 0)"
  if [ "$running" = 0 ]; then echo "SMOKE $key skipped no started machine (min_machines_running = 0, optional app)"; exit 0; fi
fi
for i in $(seq 1 "$retries"); do
  if probe > /dev/null 2>&1; then echo "SMOKE $key ok $mode ${path} (attempt $i)"; exit 0; fi
  echo "attempt $i/$retries: $key not healthy yet"; [ "$i" -lt "$retries" ] && sleep "$delay"
done
echo "SMOKE $key FAIL $mode ${path} after $retries attempts"; exit 1
