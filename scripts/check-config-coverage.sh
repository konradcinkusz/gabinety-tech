#!/usr/bin/env bash
# Fails when a configuration key a deployed service needs has no source in the estate, or when the code reads a key nobody classified (deploy/config-keys.map).
#   1. every `required` key of an app has a source: an uncommented [env] line of deploy/fly/<app>.fly.toml, or a row for the app in deploy/secrets.map
#   2. every `fake-only` key has NO source (it must not leak into a deployed estate)
#   3. every key the app's code reads (Configuration["A:B"], GetConnectionString("x"), GetValue<T>("A:B")) is listed in config-keys.map for the app or for "*"
#   4. every listed key is still read by the code (a literal of the key, or of its parent section, in the app's source): a stale row is an error
# Usage: scripts/check-config-coverage.sh [--root DIR]     No values are read or printed: names only.
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
[ "${1:-}" = "--root" ] && root="$2"
map="$root/deploy/config-keys.map" estate="$root/deploy/estate.conf" secrets="$root/deploy/secrets.map"
fail=0
err() { echo "FAIL $*"; fail=1; }
env_name() { echo "${1//:/__}"; }

# source directories of one app: its own src/ paths from estate.conf + the shared host libraries
dirs_of() {
  local app="$1" row
  row="$(sed 's/^#cutover# //' "$estate" | awk -v a="$app" '$1 == a {print $7}')"
  [ -n "$row" ] || return 1
  tr ',' '\n' <<< "$row" | grep '^src/.*/$'
  # the Blazor host reads its own options (WebOptions), not the backends' ServiceDefaults.Edge keys
  [ "$app" = web-blazor ] || { echo src/Gabinety.ServiceDefaults/; echo src/Gabinety.ServiceDefaults.Edge/; }
}
has_source() { # app env-name
  local app="$1" n="$2" toml="$root/deploy/fly/$1.fly.toml"
  grep -Eq "^[[:space:]]*${n}[[:space:]]*=" "$toml" 2>/dev/null && return 0
  awk -v a="$app" -v n="$n" '$1 == a && $2 == n {f=1} END {exit !f}' "$secrets" && return 0
  return 1
}
scan_keys() { # dirs on stdin -> keys read by the code
  while read -r d; do [ -d "$root/$d" ] && grep -rhoE --include=*.cs \
    "(Configuration|config|Config|cfg|configuration)\[\"[A-Za-z_]+(:[A-Za-z0-9:]+)*\"\]|GetConnectionString\(\"[^\"]+\"\)|GetValue<[A-Za-z?]+>\(\"[^\"]+\"" "$root/$d"; done |
    sed -E 's/^[A-Za-z]+\["//;s/"\]$//;s/^GetConnectionString\("(.*)"\)$/ConnectionStrings:\1/;s/^GetValue<[A-Za-z?]+>\("//;s/"$//' | sort -u
}
literal_in() { # app key : the key, or a parent section of it, appears as a string literal in the app's source (ConnectionStrings:x: the name)
  local app="$1" key="$2" k="$2" dirs; dirs="$(dirs_of "$app")" || return 1
  while true; do
    while read -r d; do [ -d "$root/$d" ] && grep -rqF --include=*.cs "\"$k\"" "$root/$d" && return 0
      [ -d "$root/$d" ] && [[ "$k" == *:* ]] && grep -rqF --include=*.cs "\$\"${k%:*}:{" "$root/$d" && return 0; done <<< "$dirs"
    [[ "$k" == *:* ]] || break
    k="${k%:*}"
  done
  [[ "$key" == ConnectionStrings:* ]] && while read -r d; do [ -d "$root/$d" ] && grep -rqF --include=*.cs "\"${key#*:}\"" "$root/$d" && return 0; done <<< "$dirs"
  return 1
}

[ -f "$map" ] || { echo "FAIL $map is missing"; exit 1; }
apps="$(awk '!/^#/ && NF && $1 != "*" && $1 != "backends" {print $1}' "$map" | sort -u)"
applies() { [ "$1" = "$2" ] || [ "$1" = "*" ] || { [ "$1" = backends ] && [ "$2" != web-blazor ]; }; } # row app, this app
for app in $apps; do
  [ -f "$root/deploy/fly/$app.fly.toml" ] || { err "$app: deploy/fly/$app.fly.toml is missing"; continue; }
  while read -r a key need _; do
    case "$a" in ''|'#'*) continue;; esac
    applies "$a" "$app" || continue
    n="$(env_name "$key")"
    case "$need" in
      required) has_source "$app" "$n" || err "$app: required key $key has no source (set $n in deploy/fly/$app.fly.toml [env] or add a row to deploy/secrets.map)";;
      fake-only) has_source "$app" "$n" && err "$app: $key is for the fake profile only but the estate sets $n";;
      optional) ;;
      *) err "config-keys.map: $a $key has the unknown need '$need'";;
    esac
    # a key listed for one app only must still be read there ("*" rows are checked against every app)
    if [ "$a" = "$app" ] && ! literal_in "$app" "$key"; then
      case "$key" in Llm:*|Copilot:*) literal_in assistant "$key" || err "$app: $key is no longer read by the code";; *) err "$app: $key is listed but the code of $app does not read it";; esac
    fi
  done < "$map"
  # 3: the scan
  listed="$(awk -v a="$app" '!/^#/ && NF {print $1, $2}' "$map" | while read -r ra rk; do applies "$ra" "$app" && echo "$rk"; done)"
  while read -r key; do
    [ -n "$key" ] || continue
    grep -qxF "$key" <<< "$listed" || err "$app: the code reads $key but deploy/config-keys.map does not classify it (required, optional or fake-only)"
  done < <(dirs_of "$app" | scan_keys)
done
# the "*" keys are about the .NET services only: one of them must be read by each
if [ "$fail" = 0 ]; then echo "ok   config coverage: $(awk '!/^#/ && NF && $3 == "required"' "$map" | wc -l) required keys, each with a source; every key the code reads is classified"; fi
exit "$fail"
