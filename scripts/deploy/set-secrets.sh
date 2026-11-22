#!/usr/bin/env bash
# T-09.03: set the secrets of one app from the GitHub environment secrets, STAGED (applied by the deploy that follows). Reads deploy/secrets.map.
# Never prints a value: only Fly secret names and GitHub secret names. Every value (and every line of a multi-line one) is registered with ::add-mask::.
# Usage: set-secrets.sh <app-key> <fly-app> [--map FILE] [--dry-run]
# Env:   GH_SECRETS_JSON  the `secrets` context as JSON (toJSON(secrets))     FLYCTL  (default flyctl)
set -euo pipefail
key="${1:?usage: set-secrets.sh <app-key> <fly-app> [--map FILE] [--dry-run]}"; app="${2:?}"; shift 2
map="deploy/secrets.map" dry=0
while [ $# -gt 0 ]; do case "$1" in --map) map="$2"; shift 2;; --dry-run) dry=1; shift;; *) echo "unknown argument $1" >&2; exit 2;; esac; done
: "${GH_SECRETS_JSON:?GH_SECRETS_JSON is not set}"
fly="${FLYCTL:-flyctl}"

mask() { local v="$1" line; while IFS= read -r line; do [ -n "$line" ] && printf '::add-mask::%s\n' "$line"; done <<< "$v"; return 0; }
secret() { jq -r --arg n "$1" '.[$n] // empty' <<< "$GH_SECRETS_JSON"; }

names=() values=() missing=()
while read -r a name need template; do
  case "$a" in ''|'#'*) continue;; esac
  [ "$a" = "$key" ] || continue
  rest="$template" out="" skip=0
  # a leading @if(NAME,NAME) gate: the row is skipped (silently, not "missing") unless every named GitHub secret is set; the names are not part of the value.
  # It keeps a group of keys all-or-nothing (a half-set group would stop the host at startup, or claim a feature that is not there).
  if [[ "$rest" == '@if('* ]]; then
    gate="${rest#@if(}"; gate="${gate%%)*}"; rest="${rest#*)}"
    IFS=, read -r -a gates <<< "$gate"
    for g in "${gates[@]}"; do [ -n "$(secret "$g")" ] || { skip=2; break; }; done
    if [ "$skip" = 2 ]; then echo "skip $name: gated on ${gate//,/ and }, not all set"; continue; fi
  fi
  # expand ${NAME} left to right without eval
  # shellcheck disable=SC2016  # a literal ${ is what we look for
  while [[ "$rest" == *'${'* ]]; do
    pre="${rest%%\$\{*}"; tail="${rest#*\$\{}"; ref="${tail%%\}*}"; rest="${tail#*\}}"
    val="$(secret "$ref")"
    if [ -z "$val" ]; then skip=1; missing+=("$ref:$need:$name"); fi
    out+="$pre$val"
  done
  out+="$rest"
  if [ "$skip" = 1 ]; then continue; fi
  mask "$out"; names+=("$name"); values+=("$out")
done < "$map"

fail=0
for m in ${missing[@]+"${missing[@]}"}; do
  IFS=: read -r ref need name <<< "$m"
  if [ "$need" = required ]; then echo "FAIL: GitHub environment secret $ref is not set (needed for $name on $key)" >&2; fail=1
  else echo "skip $name: optional, GitHub secret $ref is not set"; fi
done
[ "$fail" = 0 ] || exit 1
if [ "${#names[@]}" = 0 ]; then echo "no secrets for $key"; exit 0; fi
args=(); for i in "${!names[@]}"; do args+=("${names[$i]}=${values[$i]}"); done
echo "staging ${#names[@]} secret(s) on $app: ${names[*]}"
[ "$dry" = 1 ] && exit 0
"$fly" secrets set --stage --app "$app" "${args[@]}" > /dev/null
