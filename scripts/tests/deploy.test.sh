#!/usr/bin/env bash
# Tests of scripts/deploy/*.sh (T-09.03). Plain bash, no framework; stubs stand in for flyctl and curl. Run: scripts/tests/deploy.test.sh
set -uo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"; d="$root/scripts/deploy"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
pass=0 fail=0
ok()  { pass=$((pass+1)); echo "ok   $1"; }
bad() { fail=$((fail+1)); echo "FAIL $1"; }
check() { local name="$1"; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
has() { grep -Fq -- "$2" <<< "$1"; }

# ---------- detect-changes ----------
all="$tmp/all.txt"; sed -n 's/^[a-z]* *\(gabinety-[a-z-]*\) .*/\1/p' "$root/deploy/estate.conf" > "$all"
run_detect() { # <changed-files-content> <existing-file> [base]
  printf '%s' "$1" > "$tmp/changed"; : > "$tmp/out"
  "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$2" --head HEAD ${3+--base "$3"} --changed-files "$tmp/changed" --github-output "$tmp/out" > "$tmp/table"
}
sel() { grep -qx "$1=true" "$tmp/out"; }
nosel() { grep -qx "$1=false" "$tmp/out"; }

run_detect $'src/Gabinety.Booking/Foo.cs\n' "$all" v1
check "booking change selects booking only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && nosel directory && nosel web && nosel postgres && nosel authservice && nosel reputation && nosel assistant"
check "build_matrix holds only selected built apps" grep -qx 'build_matrix=\[{"key":"booking","dockerfile":"src/Gabinety.Booking/Dockerfile","context":"."}\]' "$tmp/out"
run_detect $'Directory.Packages.props\n' "$all" v1
check "shared path selects every dotnet app, not web/postgres/authservice" bash -c "$(declare -f sel nosel); tmp=$tmp; for a in directory booking reputation assistant; do sel \$a || exit 1; done; for a in web postgres authservice; do nosel \$a || exit 1; done"
run_detect $'src/Gabinety.Contracts/X.cs\nsrc/Gabinety.ServiceDefaults/Y.cs\n' "$all" v1
check "Contracts and ServiceDefaults are shared" bash -c "$(declare -f sel); tmp=$tmp; sel directory && sel assistant"
run_detect $'src/Gabinety.Llm/A.cs\n' "$all" v1
check "Llm selects assistant only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel assistant && nosel booking && nosel directory"
run_detect $'src/Gabinety.Billing/A.cs\n' "$all" v1
check "Billing selects booking" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && nosel reputation"
run_detect $'web/src/app/page.tsx\n' "$all" v1
check "web change selects web only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel web && nosel directory"
run_detect $'deploy/fly/init-databases.sql\n' "$all" v1
check "init script selects postgres" bash -c "$(declare -f sel nosel); tmp=$tmp; sel postgres && nosel authservice"
run_detect $'docs/README.md\nsrc/Gabinety.BookingX/a.cs\n' "$all" v1
check "unrelated and look-alike paths select nothing" bash -c "$(declare -f nosel); tmp=$tmp; for a in directory booking reputation assistant web postgres authservice; do nosel \$a || exit 1; done"
check "empty build matrix when nothing is selected" grep -qx 'build_matrix=\[\]' "$tmp/out"
grep -v booking "$all" > "$tmp/nobooking"
run_detect $'docs/README.md\n' "$tmp/nobooking" v1
check "an app that does not exist is always selected" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && nosel directory"
check "the reason names the missing app" has "$(cat "$tmp/table")" "Fly app does not exist"
: > "$tmp/none"
run_detect $'docs/README.md\n' "$tmp/none" v1
check "cold estate selects everything" bash -c "$(declare -f sel); tmp=$tmp; for a in postgres authservice directory booking reputation assistant web; do sel \$a || exit 1; done"
: > "$tmp/out"; "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --changed-files /dev/null --github-output "$tmp/out" > /dev/null
check "no changes and all apps exist selects nothing" bash -c "$(declare -f nosel); tmp=$tmp; nosel web && nosel booking"
# without --changed-files and without --base: first tag, everything
: > "$tmp/out"; "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --github-output "$tmp/out" > "$tmp/table"
check "no previous tag selects everything" bash -c "$(declare -f sel); tmp=$tmp; sel web && sel postgres"
# a real git diff between two commits
g="$tmp/repo"; git init -q "$g"; (cd "$g" && git config user.email t@t && git config user.name t && mkdir -p web src/Gabinety.Booking && echo a > web/a && echo a > src/Gabinety.Booking/a && git add -A && git commit -qm one && git tag v1 && echo b >> web/a && git add -A && git commit -qm two)
: > "$tmp/out"; (cd "$g" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --base v1 --github-output "$tmp/out" > "$tmp/table")
check "git diff between tags selects web only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel web && nosel booking"
: > "$tmp/out"; (cd "$g" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --base nosuchref --github-output "$tmp/out" > "$tmp/table")
check "an unreadable base selects everything" bash -c "$(declare -f sel); tmp=$tmp; sel booking && sel web"

# ---------- estate and map consistency ----------
check "secrets.map apps are all in estate.conf" bash -c "
  for a in \$(awk '!/^#/ && NF {print \$1}' '$root/deploy/secrets.map' | sort -u); do grep -Eq \"^\$a +\" '$root/deploy/estate.conf' || { echo \"unknown app \$a\"; exit 1; }; done"
check "secrets.map holds no value (only \${NAME} references and fixed text)" bash -c "! grep -Ev '^#|^\$' '$root/deploy/secrets.map' | grep -Eq 'BEGIN|password=[^\$]'"

# ---------- the two workflows pin the same flyctl ----------
check "flyctl version is the same in deploy.yml and _deploy-app.yml" bash -c "
  a=\$(sed -n 's/^  FLYCTL_VERSION: \"\\(.*\\)\"/\\1/p' '$root/.github/workflows/deploy.yml'); b=\$(sed -n 's/^          version: \"\\(.*\\)\"/\\1/p' '$root/.github/workflows/_deploy-app.yml'); [ -n \"\$a\" ] && [ \"\$a\" = \"\$b\" ]"

# ---------- check-placeholders ----------
printf 'app = "x"\n# <comment-placeholder>\n[env]\n  A = "https://<web-public-host>"\n' > "$tmp/ph.toml"
printf 'app = "x"\n# <comment-placeholder>\n[env]\n  A = "https://x.fly.dev"\n' > "$tmp/clean.toml"
check "placeholder in a value is refused" bash -c "! '$d/check-placeholders.sh' '$tmp/ph.toml' 2>/dev/null"
check "a placeholder in a comment is fine" "$d/check-placeholders.sh" "$tmp/clean.toml"
check "a missing file is refused" bash -c "! '$d/check-placeholders.sh' '$tmp/none.toml' 2>/dev/null"

# ---------- ensure-app (flyctl stub) ----------
cat > "$tmp/fly" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"
case "$1 $2" in
  "apps list") echo "${STUB_APPS:-[]}";;
  "volumes list") echo "${STUB_VOLS:-[]}";;
esac
STUB
chmod +x "$tmp/fly"
export FLYCTL="$tmp/fly" STUB_LOG="$tmp/log" FLY_ORG=acme
: > "$tmp/log"; STUB_APPS='[{"Name":"other"}]' "$d/ensure-app.sh" gabinety-booking-staging > /dev/null
check "ensure-app creates a missing app in the org" grep -qx 'apps create gabinety-booking-staging --org acme' "$tmp/log"
: > "$tmp/log"; STUB_APPS='[{"Name":"gabinety-booking-staging"}]' "$d/ensure-app.sh" gabinety-booking-staging > /dev/null
check "ensure-app leaves an existing app alone" bash -c "! grep -q 'apps create' '$tmp/log'"
: > "$tmp/log"; STUB_APPS='[{"name":"gabinety-postgres-staging"}]' "$d/ensure-app.sh" gabinety-postgres-staging pgdata 5 fra > /dev/null
check "ensure-app creates a missing volume" grep -qx 'volumes create pgdata --app gabinety-postgres-staging --region fra --size 5 --yes' "$tmp/log"
: > "$tmp/log"; STUB_APPS='[{"name":"gabinety-postgres-staging"}]' STUB_VOLS='[{"name":"pgdata"}]' "$d/ensure-app.sh" gabinety-postgres-staging pgdata > /dev/null
check "ensure-app keeps an existing volume" bash -c "! grep -q 'volumes create' '$tmp/log'"

# ---------- set-secrets ----------
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"s3cr3tpw","INTERNAL_KEY_DIRECTORY":"dirkey","FLY_API_TOKEN":"tok","AUTH_JWT_PRIVATE_KEY_PEM":"-----BEGIN PRIVATE KEY-----\nLINE2\n-----END PRIVATE KEY-----"}'
: > "$tmp/log"; out="$("$d/set-secrets.sh" directory gabinety-directory-staging --map "$root/deploy/secrets.map" 2>&1)"; rc=$?
check "directory secrets are staged" bash -c "[ $rc = 0 ] && grep -q '^secrets set --stage --app gabinety-directory-staging ConnectionStrings__directorydb=Host=gabinety-postgres-staging.internal;.*Password=s3cr3tpw Internal__Key=dirkey\$' '$tmp/log'"
check "values are masked" has "$out" "::add-mask::Host=gabinety-postgres-staging.internal;Port=5432;Database=directorydb;Username=postgres;Password=s3cr3tpw"
check "a value is never printed outside ::add-mask::" bash -c "! printf '%s\n' \"\$1\" | grep -v '^::add-mask::' | grep -q -e s3cr3tpw -e dirkey" _ "$out"
out="$("$d/set-secrets.sh" booking gabinety-booking-staging --map "$root/deploy/secrets.map" 2>&1)"; rc=$?
check "a missing required secret fails and names it, not its value" bash -c "[ $rc = 1 ]" && check "the failure names INTERNAL_KEY_BOOKING" has "$out" "INTERNAL_KEY_BOOKING"
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"pw","INTERNAL_KEY_BOOKING":"bk","INTERNAL_KEY_DIRECTORY":"dk"}'
: > "$tmp/log"; out="$("$d/set-secrets.sh" booking gabinety-booking-staging --map "$root/deploy/secrets.map" 2>&1)"
check "an optional secret that is unset is skipped" has "$out" "skip Payments__WebhookSecret"
check "booking stages three secrets" has "$out" "staging 3 secret(s)"
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"pw","AUTH_JWT_PRIVATE_KEY_PEM":"-----BEGIN X-----\nBODYLINE\n-----END X-----"}'
out="$("$d/set-secrets.sh" authservice gabinety-authservice-staging --map "$root/deploy/secrets.map" --dry-run 2>&1)"
check "every line of a multi-line secret is masked" has "$out" "::add-mask::BODYLINE"
check "dry run does not call flyctl" bash -c "! grep -q 'authservice' '$tmp/log' || [ \"\$(grep -c 'gabinety-authservice-staging' '$tmp/log')\" = 0 ]"
check "an app without rows has nothing to do" bash -c "'$d/set-secrets.sh' web gabinety-web-staging --map '$root/deploy/secrets.map' | grep -q 'no secrets for web'"

# ---------- smoke (flyctl and curl stubs) ----------
cat > "$tmp/fly" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"
case "$1" in
  ssh) [ -f "$STUB_HEALTHY" ] ;;
  machines) echo "${STUB_MACHINES:-[]}" ;;
esac
STUB
export STUB_HEALTHY="$tmp/healthy"
export SMOKE_RETRIES=3 SMOKE_DELAY=0
: > "$tmp/log"; rm -f "$tmp/healthy"
check "smoke ssh fails after the retries" bash -c "! '$d/smoke.sh' directory gabinety-directory-staging ssh /alive > '$tmp/s.txt'; grep -q 'SMOKE directory FAIL' '$tmp/s.txt'"
check "smoke retried 3 times" bash -c "[ \"\$(grep -c '^ssh console' '$tmp/log')\" = 3 ]"
touch "$tmp/healthy"
check "smoke ssh passes when healthy" bash -c "'$d/smoke.sh' directory gabinety-directory-staging ssh /alive | grep -q 'SMOKE directory ok'"
check "smoke ssh probes the given path" grep -q 'GET /alive HTTP/1.0' "$tmp/log"
check "smoke pg uses pg_isready" bash -c "'$d/smoke.sh' postgres gabinety-postgres-staging ssh-pg - | grep -q ok; grep -q pg_isready '$tmp/log'"
check "optional app with no started machine is skipped, not failed" bash -c "STUB_MACHINES='[{\"state\":\"stopped\"}]' '$d/smoke.sh' assistant gabinety-assistant-staging ssh-optional /alive | grep -q 'SMOKE assistant skipped'"
check "optional app with a started machine is probed" bash -c "STUB_MACHINES='[{\"state\":\"started\"}]' '$d/smoke.sh' assistant gabinety-assistant-staging ssh-optional /alive | grep -q 'SMOKE assistant ok'"
mkdir -p "$tmp/bin"; cat > "$tmp/bin/curl" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"; [ "${STUB_CURL_OK:-1}" = 1 ]
STUB
chmod +x "$tmp/bin/curl"
: > "$tmp/log"
check "smoke public calls the public URL and path" bash -c "PATH='$tmp/bin':\$PATH '$d/smoke.sh' web gabinety-web-staging public /api/health | grep -q 'SMOKE web ok'; grep -q 'https://gabinety-web-staging.fly.dev/api/health' '$tmp/log'"
check "smoke public honours PUBLIC_URL" bash -c "PATH='$tmp/bin':\$PATH PUBLIC_URL=https://example.test/ '$d/smoke.sh' web gabinety-web-staging public /api/health >/dev/null; grep -q 'https://example.test/api/health' '$tmp/log'"
check "smoke public fails when curl fails" bash -c "! PATH='$tmp/bin':\$PATH STUB_CURL_OK=0 '$d/smoke.sh' web gabinety-web-staging public /api/health >/dev/null"

# ---------- verify-default-branch ----------
r="$tmp/vrepo"; git init -q -b main "$r"; (cd "$r" && git config user.email t@t && git config user.name t && git commit -q --allow-empty -m a && git update-ref refs/remotes/origin/main HEAD && git checkout -q -b side && git commit -q --allow-empty -m b)
main_sha="$(git -C "$r" rev-parse main)"; side_sha="$(git -C "$r" rev-parse side)"
check "a commit on the default branch passes" bash -c "cd '$r' && '$d/verify-default-branch.sh' $main_sha main >/dev/null"
check "a commit off the default branch fails" bash -c "cd '$r' && ! '$d/verify-default-branch.sh' $side_sha main >/dev/null 2>&1"
check "an unfetched default branch fails" bash -c "cd '$r' && ! '$d/verify-default-branch.sh' $main_sha nope >/dev/null 2>&1"

# ---------- summary ----------
check "summary passes when all succeeded or were skipped" bash -c "NEEDS_JSON='{\"deploy-web\":{\"result\":\"success\"},\"deploy-booking\":{\"result\":\"skipped\"}}' '$d/summary.sh' v1.0.0 | grep -q 'not selected'"
check "summary fails on a failed job" bash -c "! NEEDS_JSON='{\"deploy-web\":{\"result\":\"failure\"}}' '$d/summary.sh' v1.0.0 >/dev/null"

echo; echo "$pass passed, $fail failed"; [ "$fail" = 0 ]
