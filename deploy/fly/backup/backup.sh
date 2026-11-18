#!/usr/bin/env bash
# Daily dump of every database of deploy/fly/init-databases.sql to an S3-compatible object store (T-10.05, SPEC 4.3).
#
# For each database: pg_dump custom format (-Fc, compressed) taken in ONE repeatable-read snapshot together with a per-table row-count
# sidecar (<db>-<ts>.counts.tsv) computed in the SAME snapshot, so restore-check.sh can compare the restored counts with the source exactly.
# Upload tool: rclone (https://rclone.org), any S3-compatible store. The sidecar is uploaded LAST: a dump without its sidecar is incomplete.
#
# Configuration, all from the environment, never from arguments, never echoed (this script has no `set -x`; do not add one):
#   PGHOST PGPORT PGUSER PGPASSWORD   libpq connection to the PostgreSQL instance (PGPASSWORD is a secret)
#   BACKUP_REMOTE                     rclone path "remote:bucket/prefix", e.g. backup:<BUCKET>/staging
#   RCLONE_CONFIG_BACKUP_TYPE=s3, RCLONE_CONFIG_BACKUP_PROVIDER, RCLONE_CONFIG_BACKUP_ENDPOINT, RCLONE_CONFIG_BACKUP_REGION   (not secret)
#   RCLONE_CONFIG_BACKUP_ACCESS_KEY_ID, RCLONE_CONFIG_BACKUP_SECRET_ACCESS_KEY                                                   (SECRETS)
#   BACKUP_DATABASES                  optional, space separated; default: the five of init-databases.sql
# Exit status: 0 only when every database was dumped, uploaded and verified.
set -euo pipefail
export RCLONE_CONFIG="${RCLONE_CONFIG:-/dev/null}"   # the remote comes from RCLONE_CONFIG_BACKUP_* only; silences "config file not found"
umask 077

DEFAULT_DATABASES="authdb directorydb bookingdb reputationdb assistantdb"
DATABASES="${BACKUP_DATABASES:-$DEFAULT_DATABASES}"
: "${BACKUP_REMOTE:?BACKUP_REMOTE is required (rclone path remote:bucket/prefix)}"
: "${PGHOST:?PGHOST is required}"
: "${PGUSER:?PGUSER is required}"
[ -n "${PGPASSWORD:-}" ] || { echo "PGPASSWORD is required (set it as a secret)" >&2; exit 2; }
export PGCONNECT_TIMEOUT="${PGCONNECT_TIMEOUT:-15}"

log() { printf '%s backup %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$*"; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# One line per table "T<TAB>schema.table<TAB>count", one per constraint type "C<TAB>contype<TAB>count", one per exclusion constraint "X<TAB>name".
read -r -d '' COUNTS_SQL <<'SQL' || true
SELECT 'T' || E'\t' || n.nspname || '.' || c.relname || E'\t' ||
       (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname), false, true, '')))[1]::text
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r', 'p') AND NOT c.relispartition AND n.nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast')
ORDER BY 1;
SELECT 'C' || E'\t' || co.contype::text || E'\t' || count(*)
FROM pg_constraint co JOIN pg_namespace n ON n.oid = co.connamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') GROUP BY co.contype ORDER BY 1;
SELECT 'X' || E'\t' || co.conname FROM pg_constraint co JOIN pg_namespace n ON n.oid = co.connamespace
WHERE co.contype = 'x' AND n.nspname NOT IN ('pg_catalog', 'information_schema') ORDER BY 1;
SQL

backup_one() {
  local db="$1" ts base snap line seen_end=0
  ts="$(date -u +%Y%m%dT%H%M%SZ)"; base="$db-$ts"
  # The coprocess holds the snapshot open while pg_dump runs.
  coproc SNAP { psql -X -q -A -t -v ON_ERROR_STOP=1 -d "$db"; }
  printf 'BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;\nSELECT pg_export_snapshot();\n' >&"${SNAP[1]}"
  IFS= read -r snap <&"${SNAP[0]}" || { log "FAIL $db: cannot open a snapshot"; return 1; }
  [ -n "$snap" ] || { log "FAIL $db: empty snapshot id"; return 1; }

  printf '%s\nSELECT %s;\n' "$COUNTS_SQL" "'--END--'" >&"${SNAP[1]}"
  : > "$WORK/$base.counts.tsv"
  while IFS= read -r line <&"${SNAP[0]}"; do
    if [ "$line" = "--END--" ]; then seen_end=1; break; fi
    printf '%s\n' "$line" >> "$WORK/$base.counts.tsv"
  done
  [ "$seen_end" = 1 ] || { log "FAIL $db: row counts did not finish"; return 1; }

  pg_dump --format=custom --compress=6 --no-sync --snapshot="$snap" --dbname="$db" --file="$WORK/$base.dump" \
    || { log "FAIL $db: pg_dump exit status $?"; return 1; }
  printf '\\q\n' >&"${SNAP[1]}"; wait "$SNAP_PID" 2>/dev/null || true

  pg_restore --list "$WORK/$base.dump" >/dev/null || { log "FAIL $db: the dump is not readable by pg_restore"; return 1; }
  grep -q '^T	' "$WORK/$base.counts.tsv" || { log "FAIL $db: no table counted"; return 1; }

  rclone copyto "$WORK/$base.dump" "$BACKUP_REMOTE/$db/$base.dump" || { log "FAIL $db: upload of the dump"; return 1; }
  rclone copyto "$WORK/$base.counts.tsv" "$BACKUP_REMOTE/$db/$base.counts.tsv" || { log "FAIL $db: upload of the counts"; return 1; }
  rclone check --one-way --include "$base.*" "$WORK" "$BACKUP_REMOTE/$db" >/dev/null 2>&1 || { log "FAIL $db: the uploaded files differ from the local ones"; return 1; }
  log "ok $db $base dump_bytes=$(stat -c %s "$WORK/$base.dump") tables=$(grep -c '^T	' "$WORK/$base.counts.tsv")"
  rm -f "$WORK/$base".*
}

failed=0
for db in $DATABASES; do
  case "$db" in *[!a-z0-9_]*|'') log "FAIL skip invalid database name"; failed=1; continue ;; esac
  backup_one "$db" || failed=1
done
[ "$failed" = 0 ] && log "done: all databases backed up" || log "done WITH FAILURES"
exit "$failed"
