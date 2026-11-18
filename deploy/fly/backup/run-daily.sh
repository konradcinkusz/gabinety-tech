#!/usr/bin/env bash
# Entrypoint of the scheduled machine: dump every database, then apply the retention rule. Exits non-zero if either step failed, so a failed
# run shows as a failed machine exit code in `fly machine status` and in `fly logs`. Retention runs only after a fully successful backup.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
"$here/backup.sh"
APPLY=1 "$here/retention.sh"
