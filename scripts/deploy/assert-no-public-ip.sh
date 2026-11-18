#!/usr/bin/env bash
# F-03 (T-10.10): an app that is private or flycast in deploy/estate.conf must have no public IP address (it would put /internal/* on the Internet). Fails naming the addresses.
# Only a private IPv6 (flycast) is allowed. Usage: assert-no-public-ip.sh <fly-app>      Env: FLYCTL (default flyctl).
set -euo pipefail
app="${1:?usage: assert-no-public-ip.sh <fly-app>}"; fly="${FLYCTL:-flyctl}"
public="$("$fly" ips list --app "$app" --json | jq -r '.[] | (.Type // .type // "") as $t | select($t | test("private") | not) | $t + " " + (.Address // .address // "")')"
if [ -n "$public" ]; then
  echo "FAIL: $app is not a public app (deploy/estate.conf) but has a public address: $public" >&2
  echo "      release it: fly ips release <address> -a $app" >&2
  exit 1
fi
echo "ok: $app has no public address"
