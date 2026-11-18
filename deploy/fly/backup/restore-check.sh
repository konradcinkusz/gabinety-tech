#!/usr/bin/env bash
# Restore one dump of backup.sh into a SCRATCH database and check it (T-10.05).
#
#   restore-check.sh <db> <dump-file> <counts-file>        # local files
#   restore-check.sh <db> --latest                         # newest complete set from $BACKUP_REMOTE (needs the RCLONE_CONFIG_BACKUP_* environment)
#
# <db> is the SOURCE database name (authdb, bookingdb, ...). The restore goes into restore_scratch_<db>_<epoch>, created here and dropped at the end
# (KEEP_SCRATCH=1 keeps it). The name must start with restore_scratch_: this script never restores into, or drops, anything else.
# Connection: libpq environment (PGHOST PGPORT PGUSER PGPASSWORD; the password is never echoed). Point PGHOST at an instance that is NOT the
# production one when you can; on one instance the scratch database only costs disk for the minutes it exists.
#
# Checks (each prints PASS or FAIL; exit 0 only if all pass):
#   1 pg_restore --exit-on-error exit status 0
#   2 the row count of EVERY table equals the count recorded in the same snapshot as the dump (the sidecar), and no table is missing or extra
#   3 the number of constraints of each type (p, f, u, c, x) equals the source's
#   4 no constraint is left NOT VALID
#   5 every foreign key has no orphan row (an anti-join per constraint, generated from pg_constraint)
#   6 for bookingdb: the exclusion constraint bookings_no_overlap exists and is a gist EXCLUDE
set -euo pipefail
export RCLONE_CONFIG="${RCLONE_CONFIG:-/dev/null}"   # the remote comes from RCLONE_CONFIG_BACKUP_* only; silences "config file not found"
umask 077

usage() { echo "usage: $0 <db> <dump-file> <counts-file> | $0 <db> --latest" >&2; exit 2; }
[ $# -ge 2 ] || usage
DB="$1"; case "$DB" in *[!a-z0-9_]*|'') echo "invalid database name" >&2; exit 2 ;; esac
: "${PGHOST:?PGHOST is required}"; : "${PGUSER:?PGUSER is required}"
[ -n "${PGPASSWORD:-}" ] || { echo "PGPASSWORD is required (set it as a secret)" >&2; exit 2; }

WORK="$(mktemp -d)"
SCRATCH="restore_scratch_${DB}_$(date +%s)"
case "$SCRATCH" in restore_scratch_*) ;; *) echo "internal: bad scratch name" >&2; exit 2 ;; esac

cleanup() {
  if [ "${KEEP_SCRATCH:-0}" != 1 ]; then psql -X -q -d postgres -c "DROP DATABASE IF EXISTS \"$SCRATCH\"" >/dev/null 2>&1 || echo "WARN could not drop $SCRATCH" >&2; fi
  rm -rf "$WORK"
}
trap cleanup EXIT

if [ "$2" = "--latest" ]; then
  : "${BACKUP_REMOTE:?BACKUP_REMOTE is required for --latest}"
  latest="$(rclone lsf --files-only "$BACKUP_REMOTE/$DB" | sed -n 's/^\(.*\)\.counts\.tsv$/\1/p' | sort | tail -n 1)"
  [ -n "$latest" ] || { echo "no complete backup set of $DB in the remote" >&2; exit 1; }
  rclone copyto "$BACKUP_REMOTE/$DB/$latest.dump" "$WORK/in.dump"
  rclone copyto "$BACKUP_REMOTE/$DB/$latest.counts.tsv" "$WORK/in.counts.tsv"
  DUMP="$WORK/in.dump"; COUNTS="$WORK/in.counts.tsv"; echo "set: $latest"
else
  [ $# -eq 3 ] || usage
  DUMP="$2"; COUNTS="$3"
  [ -r "$DUMP" ] && [ -r "$COUNTS" ] || { echo "dump or counts file not readable" >&2; exit 2; }
fi

fail=0
pass() { printf 'PASS  %s\n' "$*"; }
bad()  { printf 'FAIL  %s\n' "$*"; fail=1; }
q() { psql -X -q -A -t -v ON_ERROR_STOP=1 -d "$SCRATCH" "$@"; }

psql -X -q -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$SCRATCH\"" || { echo "cannot create the scratch database" >&2; exit 1; }
echo "scratch database: $SCRATCH  (dump: $(stat -c %s "$DUMP") bytes)"

# 1
if pg_restore --exit-on-error --no-owner --no-privileges --dbname="$SCRATCH" "$DUMP" 2>"$WORK/restore.err"; then pass "pg_restore exit status 0"
else bad "pg_restore exit status $? (first lines of stderr follow)"; head -n 10 "$WORK/restore.err" | sed 's/^/        /'; fi

# 2, 3: recompute the sidecar queries on the restored copy. The SQL is read from backup.sh so the two can never drift.
sed -n "/^read -r -d '' COUNTS_SQL <<'SQL'/,/^SQL\$/p" "$(dirname "$0")/backup.sh" | sed '1d;$d' > "$WORK/counts.sql"
[ -s "$WORK/counts.sql" ] || { echo "internal: cannot extract the counts SQL from backup.sh" >&2; exit 2; }
q -f "$WORK/counts.sql" | LC_ALL=C sort > "$WORK/restored.tsv"
LC_ALL=C sort "$COUNTS" > "$WORK/source.tsv"

if diff <(grep '^T	' "$WORK/source.tsv") <(grep '^T	' "$WORK/restored.tsv") >"$WORK/t.diff"; then
  pass "row counts of all $(grep -c '^T	' "$WORK/source.tsv") tables equal the source ($(awk -F'\t' '/^T/{s+=$3} END{print s+0}' "$WORK/source.tsv") rows): $(awk -F'\t' '/^T/{printf "%s=%s ", $2, $3}' "$WORK/source.tsv")"
else bad "row counts differ (< source, > restored)"; sed 's/^/        /' "$WORK/t.diff"; fi
if diff <(grep '^C	' "$WORK/source.tsv") <(grep '^C	' "$WORK/restored.tsv") >"$WORK/c.diff"; then
  pass "constraint counts per type equal the source ($(grep '^C	' "$WORK/source.tsv" | awk -F'\t' '{printf "%s=%s ", $2, $3}'))"
else bad "constraint counts differ (< source, > restored)"; sed 's/^/        /' "$WORK/c.diff"; fi

# 4
notvalid="$(q -c "SELECT conrelid::regclass || '.' || conname FROM pg_constraint WHERE NOT convalidated")"
[ -z "$notvalid" ] && pass "no constraint is NOT VALID" || { bad "constraints not validated:"; printf '%s\n' "$notvalid" | sed 's/^/        /'; }

# 5: one anti-join per foreign key (MATCH SIMPLE: a row with a NULL in the key is not checked)
fk_sql="SELECT co.conname || '|' || format('SELECT count(*) FROM %s c WHERE %s AND NOT EXISTS (SELECT 1 FROM %s p WHERE %s)',
          co.conrelid::regclass, (SELECT string_agg(format('c.%I IS NOT NULL', a.attname), ' AND ') FROM unnest(co.conkey) k(attnum) JOIN pg_attribute a ON a.attrelid = co.conrelid AND a.attnum = k.attnum),
          co.confrelid::regclass, (SELECT string_agg(format('c.%I = p.%I', ca.attname, pa.attname), ' AND ') FROM unnest(co.conkey, co.confkey) k(ck, pk)
            JOIN pg_attribute ca ON ca.attrelid = co.conrelid AND ca.attnum = k.ck JOIN pg_attribute pa ON pa.attrelid = co.confrelid AND pa.attnum = k.pk))
        FROM pg_constraint co WHERE co.contype = 'f' ORDER BY co.conname"
fk_total=0; fk_bad=0
while IFS='|' read -r name sql; do
  [ -n "$name" ] || continue
  fk_total=$((fk_total + 1))
  orphans="$(q -c "$sql")"
  [ "$orphans" = 0 ] || { fk_bad=$((fk_bad + 1)); bad "foreign key $name has $orphans orphan row(s)"; }
done < <(q -c "$fk_sql")
[ "$fk_bad" = 0 ] && pass "all $fk_total foreign keys have no orphan row" || true

# 6
if [ "$DB" = bookingdb ]; then
  def="$(q -c "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = 'bookings_no_overlap' AND contype = 'x'")"
  case "$def" in EXCLUDE\ USING\ gist*) pass "exclusion constraint bookings_no_overlap present ($(printf '%s' "$def" | cut -c1-60)...)" ;; *) bad "exclusion constraint bookings_no_overlap is missing" ;; esac
fi

echo "----"
[ "$fail" = 0 ] && echo "RESULT: restore of $DB verified" || echo "RESULT: restore of $DB FAILED"
exit "$fail"
