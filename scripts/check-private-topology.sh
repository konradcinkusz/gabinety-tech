#!/usr/bin/env bash
# F-03 (T-10.10): the private apps of deploy/estate.conf stay private in the committed config, and the web host (the only caller from outside the backends) reaches them over
# the private network: Backend__Directory/Booking/Reputation = <app>.internal:8080, Backend__Assistant = <app>.flycast. Also F-10/F-11: https Site__BaseUrl and the identity settings are in the
# deployed templates. No network, no secrets.
set -euo pipefail
cd "$(dirname "$0")/.."
fail=0; bad() { echo "FAIL: $*" >&2; fail=1; }
web=deploy/fly/web-blazor.fly.toml
# the web host reaches the private apps over the private network
for web_cfg in "$web"; do
  for k in directory booking reputation; do
    grep -Eq "^[[:space:]]*Backend__${k^} = \"http://gabinety-${k}-staging\.internal:8080\"" "$web_cfg" || bad "$web_cfg: Backend__${k^} must be http://gabinety-${k}-staging.internal:8080"
  done
  grep -Eq '^[[:space:]]*Backend__Assistant = "http://gabinety-assistant-staging\.flycast"' "$web_cfg" || bad "$web_cfg: Backend__Assistant must be http://gabinety-assistant-staging.flycast"
  grep -Eq '^[[:space:]]*Site__BaseUrl = "https://' "$web_cfg" || bad "$web_cfg: Site__BaseUrl must be an https URL (cookies are Secure only then)"
  grep -Eq '^[[:space:]]*Identity__Audience = "[^"]+"' "$web_cfg" || bad "$web_cfg: Identity__Audience must be set"
done
while read -r key _ _ _ _ _ _ _ _ network; do
  case "$key" in ''|'#'*|shared) continue;; esac
  [ "$network" = public ] && continue
  f="deploy/fly/$key.fly.toml"
  grep -q '^[[:space:]]*\[\[services\.ports\]\]' "$f" && bad "$f: a private app must not define [[services.ports]] (a public port); use [http_service] with --no-public-ips"
done < deploy/estate.conf
# the only public apps are the three of SPEC 4.3
pub="$(awk '!/^#/ && NF && $1 != "shared" && $NF == "public" {print $1}' deploy/estate.conf | sort | tr '\n' ' ')"
[ "$pub" = "authservice booking web-blazor " ] || bad "public apps in deploy/estate.conf are '$pub', expected 'authservice booking web-blazor' (SPEC 4.3; the web row of the estate is web-blazor since the cutover)"
# F-10 / F-11 (T-10.16): the settings the code cannot supply for itself stay in the deployed templates: https site URL (the session cookies are Secure only then) and,
# on every app that validates tokens, an https authority and the audience (the services also refuse to start without them, Gabinety.ServiceDefaults.Edge).
grep -Eq '^[[:space:]]*Site__BaseUrl = "https://' "$web" || bad "$web: Site__BaseUrl must be an https URL (cookies are Secure only then)"
grep -Eq '^[[:space:]]*Identity__Audience = "[^"]+"' "$web" || bad "$web: Identity__Audience must be set"
for k in directory booking reputation assistant; do
  f="deploy/fly/$k.fly.toml"
  grep -Eq '^[[:space:]]*Identity__Authority = "https://' "$f" || bad "$f: Identity__Authority must be an https URL"
  grep -Eq '^[[:space:]]*Identity__Audience = "[^"]+"' "$f" || bad "$f: Identity__Audience must be set"
done
exit $fail
