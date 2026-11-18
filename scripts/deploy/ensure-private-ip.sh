#!/usr/bin/env bash
# T-09.03: allocate the private IPv6 of an app once, so <app>.flycast resolves (docs/operations/COST.md). Idempotent. Usage: ensure-private-ip.sh <fly-app>
# Env: FLYCTL (default flyctl).
set -euo pipefail
app="${1:?usage: ensure-private-ip.sh <fly-app>}"; fly="${FLYCTL:-flyctl}"
if "$fly" ips list --app "$app" --json | jq -e '.[] | select(((.Type // .type) // "") | test("private"))' > /dev/null; then
  echo "app $app already has a private IPv6"
else
  "$fly" ips allocate-v6 --private --app "$app"
fi
