#!/usr/bin/env bash
# Local backup -> restore round trip (T-10.05). Needs: PostgreSQL server binaries (initdb, pg_ctl, psql, pg_dump, pg_restore), rclone, GNU date.
# Starts a throw-away PostgreSQL on 127.0.0.1:$PORT (password auth, a random password that is never printed), loads the synthetic fixtures,
# runs backup.sh against an rclone LOCAL-DISK "remote" (NOT S3), restore-check.sh for each database, and the negative and retention tests.
# Prints a report you can paste into docs/operations/RESTORE-LOG.md. Cleans up after itself.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; BK="$HERE/.."; REPO="$HERE/../../../.."
PORT="${PORT:-55432}"
PGBIN="${PGBIN:-$(ls -d /usr/lib/postgresql/*/bin | sort -V | tail -n 1)}"; export PATH="$PGBIN:$PATH"
T="$(mktemp -d)"; chmod 755 "$T"
as_pg() { if [ "$(id -u)" = 0 ]; then runuser -u postgres -- "$@"; else "$@"; fi; }
[ "$(id -u)" = 0 ] && chown postgres "$T"
PGPASSWORD="$(head -c 24 /dev/urandom | base64 | tr -dc 'A-Za-z0-9')"; export PGPASSWORD
trap 'as_pg pg_ctl -D "$T/data" -m fast stop >/dev/null 2>&1 || true; rm -rf "$T"' EXIT
export PGHOST=127.0.0.1 PGPORT="$PORT" PGUSER=postgres

printf '%s' "$PGPASSWORD" > "$T/pw"; chmod 644 "$T/pw"
as_pg initdb -D "$T/data" -U postgres -A scram-sha-256 --pwfile="$T/pw" >/dev/null
as_pg pg_ctl -D "$T/data" -o "-p $PORT -c listen_addresses=127.0.0.1 -c unix_socket_directories=$T" -l "$T/pg.log" -w start >/dev/null
rm -f "$T/pw"
echo "environment: $(psql --version | head -1 | sed 's/psql (PostgreSQL) /PostgreSQL client /'), server $(psql -At -d postgres -c 'show server_version'), $(rclone version | head -1), $(uname -sr), $(date -u +%Y-%m-%dT%H:%MZ)"

echo "== the database list of backup.sh equals init-databases.sql"
want="$(grep -o "('[a-z]*')" "$REPO/deploy/fly/init-databases.sql" | tr -d "()'" | sort | tr '\n' ' ')"
have="$(grep '^DEFAULT_DATABASES=' "$BK/backup.sh" | cut -d'"' -f2 | tr ' ' '\n' | sort | tr '\n' ' ')"
[ "$want" = "$have" ] && echo "PASS  $want" || { echo "FAIL  init-databases.sql: $want / backup.sh: $have"; exit 1; }

echo "== load the synthetic fixtures (init-databases.sql, then one fixture per database)"
psql -X -q -d postgres -f "$REPO/deploy/fly/init-databases.sql"
for db in authdb directorydb bookingdb reputationdb assistantdb; do psql -X -q -v ON_ERROR_STOP=1 -d "$db" -f "$HERE/fixture/$db.sql"; done

export RCLONE_CONFIG_BACKUP_TYPE=local BACKUP_REMOTE="backup:$T/objects"
echo "== backup.sh (rclone local-disk remote, not S3)"
"$BK/backup.sh"

echo "== restore-check.sh --latest for each database"
rc=0
for db in authdb directorydb bookingdb reputationdb assistantdb; do echo "--- $db"; "$BK/restore-check.sh" "$db" --latest || rc=1; done

echo "== negative test 1: a tampered counts sidecar must FAIL"
set_dir="$T/objects/directorydb"; f="$(ls "$set_dir"/*.counts.tsv | head -n 1)"
sed -i '0,/^T\t/s/^\(T\t[^\t]*\t\)[0-9]*$/\1999999/' "$f"
if "$BK/restore-check.sh" directorydb --latest >"$T/neg1.out" 2>&1; then echo "FAIL  tampered counts were accepted"; rc=1; else grep -E '^(FAIL|RESULT)' "$T/neg1.out" | sed 's/^/      /'; echo "PASS  rejected"; fi

echo "== negative test 2: a bookingdb dump WITHOUT bookings_no_overlap must FAIL"
psql -X -q -d postgres -c "CREATE DATABASE bookingdb_nocons TEMPLATE bookingdb"
psql -X -q -d bookingdb_nocons -c "ALTER TABLE bookings DROP CONSTRAINT bookings_no_overlap"
BACKUP_DATABASES=bookingdb_nocons "$BK/backup.sh" >/dev/null
mkdir -p "$T/neg2" && cp "$T"/objects/bookingdb_nocons/*.dump "$T/neg2/in.dump" && cp "$T"/objects/bookingdb/*.counts.tsv "$T/neg2/in.counts.tsv"
if "$BK/restore-check.sh" bookingdb "$T/neg2/in.dump" "$T/neg2/in.counts.tsv" >"$T/neg2.out" 2>&1; then echo "FAIL  missing constraint accepted"; rc=1; else grep -E '^(FAIL|RESULT)' "$T/neg2.out" | sed 's/^/      /'; echo "PASS  rejected"; fi

echo "== retention.sh: dry run, then apply, on a remote that holds 18 daily sets plus an old orphan dump"
R="$T/ret"; mkdir -p "$R/bookingdb"; export BACKUP_REMOTE="backup:$R"
for d in $(seq 1 18); do day="202611$(printf %02d "$d")"; : > "$R/bookingdb/bookingdb-${day}T020000Z.dump"; : > "$R/bookingdb/bookingdb-${day}T020000Z.counts.tsv"; done
: > "$R/bookingdb/bookingdb-20261010T020000Z.dump"
BACKUP_DATABASES=bookingdb NOW=20261118T130000Z "$BK/retention.sh" | sed 's/^/      /' | head -n 4
echo "      files before: $(ls "$R/bookingdb" | wc -l)"
BACKUP_DATABASES=bookingdb APPLY=1 NOW=20261118T130000Z "$BK/retention.sh" >/dev/null
kept="$(ls "$R/bookingdb" | sed -n 's/^bookingdb-\(2026[0-9]*\)T.*\.dump$/\1/p' | tr '\n' ' ')"
echo "      kept dumps after apply: $kept"
# daily: 12..18 (7 days); weekly: newest of ISO weeks 47 (Nov 16-18 -> 18), 46 (Nov 9-15 -> 15), 45 (Nov 2-8 -> 8), 44 (Oct 26-Nov 1 -> 1)
want_kept="20261101 20261108 20261112 20261113 20261114 20261115 20261116 20261117 20261118 "
[ "$kept" = "$want_kept" ] && echo "PASS  retention kept exactly 7 daily + the newest set of each of 4 ISO weeks; the old orphan was deleted" || { echo "FAIL  expected: $want_kept"; rc=1; }

[ "$rc" = 0 ] && echo "ROUND TRIP: all checks passed" || echo "ROUND TRIP: FAILURES"
exit "$rc"
