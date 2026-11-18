#!/usr/bin/env bash
# F-03 (T-10.10): the private apps of deploy/estate.conf stay private in the committed config, and the web BFF (the only caller from outside the backends) reaches them over
# the private network: Backend__Directory/Booking/Reputation = <app>.internal:8080, Backend__Assistant = <app>.flycast. No network, no secrets.
set -euo pipefail
cd "$(dirname "$0")/.."
fail=0; bad() { echo "FAIL: $*" >&2; fail=1; }
web=deploy/fly/web.fly.toml
for k in directory booking reputation; do
  grep -Eq "^[[:space:]]*Backend__${k^} = \"http://gabinety-${k}-staging\.internal:8080\"" "$web" || bad "$web: Backend__${k^} must be http://gabinety-${k}-staging.internal:8080"
done
grep -Eq '^[[:space:]]*Backend__Assistant = "http://gabinety-assistant-staging\.flycast"' "$web" || bad "$web: Backend__Assistant must be http://gabinety-assistant-staging.flycast"
while read -r key _ _ _ _ _ _ _ _ network; do
  case "$key" in ''|'#'*|shared) continue;; esac
  [ "$network" = public ] && continue
  f="deploy/fly/$key.fly.toml"
  grep -q '^[[:space:]]*\[\[services\.ports\]\]' "$f" && bad "$f: a private app must not define [[services.ports]] (a public port); use [http_service] with --no-public-ips"
done < deploy/estate.conf
# the only public apps are the three of SPEC 4.3
pub="$(awk '!/^#/ && NF && $1 != "shared" && $NF == "public" {print $1}' deploy/estate.conf | sort | tr '\n' ' ')"
[ "$pub" = "authservice booking web " ] || bad "public apps in deploy/estate.conf are '$pub', expected 'authservice booking web' (SPEC 4.3)"
exit $fail
