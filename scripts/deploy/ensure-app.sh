#!/usr/bin/env bash
# T-09.03: create the Fly app (and, for the database, its volume) when it does not exist yet, so a tag against an empty organisation provisions the estate
# (FLY-IO-DEPLOYMENT 11). Idempotent. Usage: ensure-app.sh <fly-app> [<volume-name> <size-gb> <region>]
# Env: FLYCTL (default flyctl), FLY_ORG (default personal).
set -euo pipefail
app="${1:?usage: ensure-app.sh <fly-app> [volume size-gb region]}"; vol="${2:-}"; size="${3:-5}"; region="${4:-fra}"
fly="${FLYCTL:-flyctl}"
if "$fly" apps list --json | jq -e --arg a "$app" '.[] | select((.Name // .name) == $a)' > /dev/null; then
  echo "app $app exists"
else
  "$fly" apps create "$app" --org "${FLY_ORG:-personal}"
fi
if [ -n "$vol" ]; then
  if "$fly" volumes list --app "$app" --json | jq -e --arg v "$vol" '.[] | select((.Name // .name) == $v)' > /dev/null; then
    echo "volume $vol exists on $app"
  else
    "$fly" volumes create "$vol" --app "$app" --region "$region" --size "$size" --yes
  fi
fi
