#!/usr/bin/env bash
# Retention rule for the dumps of backup.sh (T-10.05): keep 7 daily + 4 weekly, delete the rest. Dry run unless APPLY=1.
#
# A "set" is the pair <db>-<ts>.dump + <db>-<ts>.counts.tsv (ts = UTC YYYYMMDDTHHMMSSZ); a set is complete only when both exist.
#   daily  : the newest complete set of each of the KEEP_DAILY (7) most recent UTC dates that have one
#   weekly : the newest complete set of each of the KEEP_WEEKLY (4) most recent ISO weeks that have one (the current week counts)
#   an incomplete set (a dump without counts, or counts without a dump) is kept while it is younger than 24 h (it may still be uploading), then deleted
# The union is kept. At least the newest complete set is always kept; the script refuses a KEEP_DAILY below 1.
#
#   BACKUP_REMOTE=backup:<BUCKET>/staging ./retention.sh            # prints the plan, deletes nothing
#   BACKUP_REMOTE=backup:<BUCKET>/staging APPLY=1 ./retention.sh    # deletes
#   ./retention.sh --plan-stdin [NOW=YYYYMMDDTHHMMSSZ]              # reads file names on stdin, prints "KEEP|DELETE <name>" (used by the test)
# Needs GNU date (coreutils). Credentials: the same RCLONE_CONFIG_BACKUP_* environment as backup.sh, never echoed.
set -euo pipefail
export RCLONE_CONFIG="${RCLONE_CONFIG:-/dev/null}"   # the remote comes from RCLONE_CONFIG_BACKUP_* only; silences "config file not found"

KEEP_DAILY="${KEEP_DAILY:-7}"
KEEP_WEEKLY="${KEEP_WEEKLY:-4}"
DATABASES="${BACKUP_DATABASES:-authdb directorydb bookingdb reputationdb assistantdb}"
[ "$KEEP_DAILY" -ge 1 ] 2>/dev/null || { echo "KEEP_DAILY must be an integer >= 1" >&2; exit 2; }
[ "$KEEP_WEEKLY" -ge 0 ] 2>/dev/null || { echo "KEEP_WEEKLY must be an integer >= 0" >&2; exit 2; }

epoch() { date -u -d "${1:0:4}-${1:4:2}-${1:6:2} ${1:9:2}:${1:11:2}:${1:13:2}" +%s; }
isoweek() { date -u -d "${1:0:4}-${1:4:2}-${1:6:2}" +%G-W%V; }

# stdin: file names of ONE database; stdout: "KEEP name" / "DELETE name". NOW (default: now) is the reference time.
plan() {
  local now_ts="${NOW:-$(date -u +%Y%m%dT%H%M%SZ)}" now_epoch name ts
  now_epoch="$(epoch "$now_ts")"
  declare -A has_dump=() has_counts=() keep=()
  local -a all_names=()
  while IFS= read -r name; do
    [ -n "$name" ] || continue
    all_names+=("$name")
    if [[ "$name" =~ ^[a-z0-9_]+-([0-9]{8}T[0-9]{6}Z)\.dump$ ]]; then has_dump["${BASH_REMATCH[1]}"]=1
    elif [[ "$name" =~ ^[a-z0-9_]+-([0-9]{8}T[0-9]{6}Z)\.counts\.tsv$ ]]; then has_counts["${BASH_REMATCH[1]}"]=1
    fi
  done
  local -a complete=()
  while IFS= read -r ts; do [ -n "$ts" ] && complete+=("$ts"); done < <(
    for ts in "${!has_dump[@]}"; do [ -n "${has_counts[$ts]:-}" ] && echo "$ts"; done | sort -r)

  local -A day_seen=() week_seen=()
  local nd=0 nw=0 d w
  for ts in "${complete[@]}"; do   # newest first
    d="${ts:0:8}"
    if [ -z "${day_seen[$d]:-}" ] && [ "$nd" -lt "$KEEP_DAILY" ]; then day_seen[$d]=1; nd=$((nd + 1)); keep[$ts]=1; fi
    w="$(isoweek "$ts")"
    if [ -z "${week_seen[$w]:-}" ] && [ "$nw" -lt "$KEEP_WEEKLY" ]; then week_seen[$w]=1; nw=$((nw + 1)); keep[$ts]=1; fi
  done
  # Incomplete sets: keep while young.
  for ts in "${!has_dump[@]}" "${!has_counts[@]}"; do
    if [ -z "${has_dump[$ts]:-}" ] || [ -z "${has_counts[$ts]:-}" ]; then
      if [ $((now_epoch - $(epoch "$ts"))) -lt 86400 ]; then keep[$ts]=1; fi
    fi
  done
  for name in "${all_names[@]}"; do
    if [[ "$name" =~ -([0-9]{8}T[0-9]{6}Z)\.(dump|counts\.tsv)$ ]]; then
      [ -n "${keep[${BASH_REMATCH[1]}]:-}" ] && echo "KEEP $name" || echo "DELETE $name"
    else
      echo "KEEP $name"   # a file this script does not recognise is never deleted
    fi
  done
}

if [ "${1:-}" = "--plan-stdin" ]; then plan; exit 0; fi

: "${BACKUP_REMOTE:?BACKUP_REMOTE is required (rclone path remote:bucket/prefix)}"
rc=0
for db in $DATABASES; do
  case "$db" in *[!a-z0-9_]*|'') echo "invalid database name skipped" >&2; rc=1; continue ;; esac
  names="$(rclone lsf --files-only "$BACKUP_REMOTE/$db")" || { echo "cannot list $db" >&2; rc=1; continue; }
  p="$(printf '%s\n' "$names" | plan | sort -k2)"
  keep_n="$(grep -c '^KEEP .*\.dump$' <<<"$p" || true)"; del_n="$(grep -c '^DELETE ' <<<"$p" || true)"
  echo "== $db: keep $keep_n dump(s), delete $del_n file(s)$([ "${APPLY:-0}" = 1 ] || echo ' (dry run, set APPLY=1 to delete)')"
  grep '^DELETE ' <<<"$p" || true
  if [ "${APPLY:-0}" = 1 ]; then
    while read -r verb name; do
      [ "$verb" = DELETE ] && { rclone deletefile "$BACKUP_REMOTE/$db/$name" || rc=1; }
    done <<<"$p"
  fi
done
exit "$rc"
