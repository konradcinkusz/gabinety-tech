#!/usr/bin/env bash
# T-09.03: final summary table (Markdown) from the `needs` context. Usage: NEEDS_JSON='<toJSON(needs)>' summary.sh <tag> >> "$GITHUB_STEP_SUMMARY"
# Exit 1 when any job failed or was cancelled.
set -euo pipefail
tag="${1:?usage: summary.sh <tag>}"; : "${NEEDS_JSON:?}"
echo "## Deploy $tag"; echo
echo "| Job | Result |"; echo "|---|---|"
jq -r 'to_entries[] | "| \(.key) | \(if .value.result == "success" then "deployed and healthy" elif .value.result == "skipped" then "not selected (unchanged)" else .value.result end) |"' <<< "$NEEDS_JSON"
bad="$(jq -r '[to_entries[] | select(.value.result == "failure" or .value.result == "cancelled") | .key] | join(", ")' <<< "$NEEDS_JSON")"
if [ -n "$bad" ]; then echo; echo "**Not healthy: $bad**"; exit 1; fi
