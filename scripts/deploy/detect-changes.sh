#!/usr/bin/env bash
# T-09.03: which apps does this tag deploy? Reads deploy/estate.conf.
# An app is selected when: there is no previous tag (first deploy), or its Fly app does not exist (cold estate), or a path of the app (or a shared path,
# for apps with shared=yes) changed between the previous tag and this one, or the diff could not be computed (then everything is selected: safe side).
# Usage: detect-changes.sh --estate FILE --existing-apps FILE --head REF [--base REF] [--changed-files FILE] [--github-output FILE]
#   --existing-apps  one Fly app name per line (the apps of the organisation)
#   --changed-files  a list of changed paths instead of `git diff` (tests)
# Prints a table; with --github-output writes <key>=true|false for every app and build_matrix=<json> (selected apps with kind built).
set -euo pipefail
estate="" existing="" head="" base="" changed="" out=""
while [ $# -gt 0 ]; do
  case "$1" in
    --estate) estate="$2";; --existing-apps) existing="$2";; --head) head="$2";;
    --base) base="$2";; --changed-files) changed="$2";; --github-output) out="$2";;
    *) echo "unknown argument $1" >&2; exit 2;;
  esac; shift 2
done
[ -n "$estate" ] && [ -n "$existing" ] || { echo "usage: detect-changes.sh --estate FILE --existing-apps FILE --head REF [--base REF] ..." >&2; exit 2; }

files="$(mktemp)"; trap 'rm -f "$files"' EXIT
all_reason=""
if [ -n "$changed" ]; then cp "$changed" "$files"
elif [ -z "$base" ]; then all_reason="no previous tag"
elif ! git diff --name-only "$base" "$head" > "$files" 2>/dev/null; then all_reason="diff against $base failed"
fi

# touches <comma-separated paths>: does any changed file match one of them?
touches() {
  local p f
  IFS=, read -ra pats <<< "$1"
  while IFS= read -r f; do
    [ -n "$f" ] || continue
    for p in "${pats[@]}"; do
      case "$p" in */) [[ "$f" == "$p"* ]] && return 0;; *) [ "$f" = "$p" ] && return 0;; esac
    done
  done < "$files"
  return 1
}

shared_paths=""
while read -r key fly kind dockerfile context shared paths _ _ _; do
  case "$key" in ''|'#'*) continue;; shared) shared_paths="$paths"; continue;; esac
  reason=""
  if [ -n "$all_reason" ]; then reason="$all_reason"
  elif ! grep -Fxq "$fly" "$existing"; then reason="Fly app does not exist"
  elif touches "$paths"; then reason="app paths changed"
  elif [ "$shared" = yes ] && [ -n "$shared_paths" ] && touches "$shared_paths"; then reason="shared paths changed"
  fi
  sel=false; [ -n "$reason" ] && sel=true
  printf '%-12s %-30s selected=%-5s %s\n' "$key" "$fly" "$sel" "${reason:--}"
  if [ -n "$out" ]; then
    echo "$key=$sel" >> "$out"
    if [ "$sel" = true ] && [ "$kind" = built ]; then
      jq -nc --arg key "$key" --arg fly "$fly" --arg dockerfile "$dockerfile" --arg context "$context" '{key:$key,fly_app:$fly,dockerfile:$dockerfile,context:$context}'
    fi >> "$files.matrix"
  fi
done < "$estate"
if [ -n "$out" ]; then
  if [ -f "$files.matrix" ]; then echo "build_matrix=$(jq -sc . "$files.matrix")" >> "$out"; rm -f "$files.matrix"; else echo "build_matrix=[]" >> "$out"; fi
fi
