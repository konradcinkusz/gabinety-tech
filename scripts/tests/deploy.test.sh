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
all="$tmp/all.txt"; sed -n 's/^[a-z-]* *\(gabinety-[a-z-]*\) .*/\1/p' "$root/deploy/estate.conf" > "$all"
run_detect() { # <changed-files-content> <existing-file> [base]
  printf '%s' "$1" > "$tmp/changed"; : > "$tmp/out"
  "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$2" --head HEAD ${3+--base "$3"} --changed-files "$tmp/changed" --github-output "$tmp/out" > "$tmp/table"
}
sel() { grep -qx "$1=true" "$tmp/out"; }
nosel() { grep -qx "$1=false" "$tmp/out"; }

run_detect $'src/Gabinety.Booking/Foo.cs\n' "$all" v1
check "booking change selects booking only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && nosel directory && nosel web-blazor && nosel postgres && nosel authservice && nosel reputation && nosel assistant"
check "build_matrix holds only selected built apps" grep -qx 'build_matrix=\[{"key":"booking","fly_app":"gabinety-booking-staging","dockerfile":"src/Gabinety.Booking/Dockerfile","context":"."}\]' "$tmp/out"
run_detect $'Directory.Packages.props\n' "$all" v1
check "shared path selects every dotnet app and the web host, not postgres/authservice" bash -c "$(declare -f sel nosel); tmp=$tmp; for a in directory booking reputation assistant web-blazor; do sel \$a || exit 1; done; for a in postgres authservice; do nosel \$a || exit 1; done"
run_detect $'src/Gabinety.Contracts/X.cs\nsrc/Gabinety.ServiceDefaults/Y.cs\n' "$all" v1
check "Contracts and ServiceDefaults are shared" bash -c "$(declare -f sel); tmp=$tmp; sel directory && sel assistant"
run_detect $'src/Gabinety.Llm/A.cs\n' "$all" v1
check "Llm selects assistant only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel assistant && nosel booking && nosel directory"
run_detect $'src/Gabinety.Billing/A.cs\n' "$all" v1
check "Billing selects booking" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && nosel reputation"
run_detect $'src/Gabinety.Web/Components/App.razor\n' "$all" v1
check "a change in the web host selects web-blazor only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel web-blazor && nosel directory"
run_detect $'deploy/fly/init-databases.sql\n' "$all" v1
check "init script selects postgres" bash -c "$(declare -f sel nosel); tmp=$tmp; sel postgres && nosel authservice"
run_detect $'docs/README.md\nsrc/Gabinety.BookingX/a.cs\n' "$all" v1
check "unrelated and look-alike paths select nothing" bash -c "$(declare -f nosel); tmp=$tmp; for a in directory booking reputation assistant web-blazor postgres authservice; do nosel \$a || exit 1; done"
check "empty build matrix when nothing is selected" grep -qx 'build_matrix=\[\]' "$tmp/out"
grep -v booking "$all" > "$tmp/nobooking"
run_detect $'docs/README.md\n' "$tmp/nobooking" v1
check "an app that does not exist is always selected" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && nosel directory"
check "the reason names the missing app" has "$(cat "$tmp/table")" "Fly app does not exist"
: > "$tmp/none"
run_detect $'docs/README.md\n' "$tmp/none" v1
check "cold estate selects everything" bash -c "$(declare -f sel); tmp=$tmp; for a in postgres authservice directory booking reputation assistant web-blazor; do sel \$a || exit 1; done"
: > "$tmp/out"; "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --changed-files /dev/null --github-output "$tmp/out" > /dev/null
check "no changes and all apps exist selects nothing" bash -c "$(declare -f nosel); tmp=$tmp; nosel web-blazor && nosel booking"
# without --changed-files and without --base: first tag, everything
: > "$tmp/out"; "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --github-output "$tmp/out" > "$tmp/table"
check "no previous tag selects everything" bash -c "$(declare -f sel); tmp=$tmp; sel web-blazor && sel postgres"
# a real git diff between two commits
g="$tmp/repo"; git init -q "$g"; (cd "$g" && git config user.email t@t && git config user.name t && mkdir -p src/Gabinety.Web src/Gabinety.Booking && echo a > src/Gabinety.Web/a && echo a > src/Gabinety.Booking/a && git add -A && git commit -qm one && git tag v1 && echo b >> src/Gabinety.Web/a && git add -A && git commit -qm two)
: > "$tmp/out"; (cd "$g" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --base v1 --github-output "$tmp/out" > "$tmp/table")
check "git diff between tags selects web-blazor only" bash -c "$(declare -f sel nosel); tmp=$tmp; sel web-blazor && nosel booking"
: > "$tmp/out"; (cd "$g" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --base nosuchref --github-output "$tmp/out" > "$tmp/table")
check "an unreadable base selects everything" bash -c "$(declare -f sel); tmp=$tmp; sel booking && sel web-blazor"

# ---------- estate and map consistency ----------
check "secrets.map apps are all in estate.conf" bash -c "
  for a in \$(awk '!/^#/ && NF {print \$1}' '$root/deploy/secrets.map' | sort -u); do grep -Eq \"^(#cutover# )?\$a +\" '$root/deploy/estate.conf' || { echo \"unknown app \$a\"; exit 1; }; done"
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

# ---------- Booking is not deployed in Mock mode (security review F-01) ----------
check "no committed fly.toml sets Payments__Provider to Mock" bash -c "! grep -Eiq '^[^#]*Payments__Provider *= *\"mock' '$root'/deploy/fly/*.fly.toml"
check "while Payments__Provider is a placeholder the Booking deploy is refused" bash -c "
  if grep -Eq '^[^#]*Payments__Provider *= *\"<' '$root/deploy/fly/booking.fly.toml'; then ! '$d/check-placeholders.sh' '$root/deploy/fly/booking.fly.toml' 2>/dev/null; fi"

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


# ---------- estate.conf consistency with deploy/fly ----------
check "every app of estate.conf has deploy/fly/<key>.fly.toml naming its Fly app" bash -c "
  while read -r key fly rest; do case \"\$key\" in ''|'#'*|shared) continue;; esac
    f='$root/deploy/fly/'\$key'.fly.toml'; [ -f \"\$f\" ] || { echo \"missing \$f\"; exit 1; }
    grep -q \"^app = \\\"\$fly\\\"\" \"\$f\" || { echo \"\$f does not name \$fly\"; exit 1; }
  done < '$root/deploy/estate.conf'"
check "every deploy/fly/*.fly.toml is an app of estate.conf" bash -c "
  for f in '$root'/deploy/fly/*.fly.toml; do k=\$(basename \"\$f\" .fly.toml); grep -Eq \"^(#cutover# )?\$k +\" '$root/deploy/estate.conf' || { echo \"\$k not in estate.conf\"; exit 1; }; done"
check "network is public, private or flycast for every app, Assistant is flycast" bash -c "
  awk '!/^#/ && NF && \$1 != \"shared\" {print \$1, \$NF}' '$root/deploy/estate.conf' | grep -Evq ' (public|private|flycast)\$' && exit 1
  awk '\$1 == \"assistant\" {print \$NF}' '$root/deploy/estate.conf' | grep -qx flycast"

# ---------- the web host row (ADR-0010, the cutover): the Blazor Web App is the estate's web app ----------
check "the estate has the web-blazor row live, no commented cutover row and no Next.js web row" bash -c "grep -q '^web-blazor ' '$root/deploy/estate.conf' && ! grep -q '^#cutover#' '$root/deploy/estate.conf' && ! grep -Eq '^web +' '$root/deploy/estate.conf'"
web_row_ok() { # fields of the row: key fly_app kind dockerfile context shared paths smoke smoke_path network
  local row; row="$(grep '^web-blazor ' "$root/deploy/estate.conf")" || { echo "no web-blazor row"; return 1; }
  local f; read -r -a f <<< "$row"
  [ "${#f[@]}" = 10 ] || { echo "fields: ${#f[@]}"; return 1; }
  [ "${f[1]}" = gabinety-web-blazor-staging ] && [ "${f[2]}" = built ] && [ -f "$root/${f[3]}" ] && [ "${f[7]}" = public ] && [ "${f[8]}" = /alive ] && [ "${f[9]}" = public ] &&
    grep -q '^app = "gabinety-web-blazor-staging"' "$root/deploy/fly/web-blazor.fly.toml"
}
check "the web-blazor row is well formed: 10 fields, a Dockerfile and a config that exist and name the app, public, /alive" web_row_ok
check "the row has the shape of a built public app" bash -c "[ \"\$(awk '\$1==\"web-blazor\" {print \$3, \$(NF)}' '$root/deploy/estate.conf')\" = \"built public\" ]"
check "the web row is last (SPEC 9: the web app deploys after the services it calls)" bash -c "[ \"\$(awk '!/^#/ && NF {k=\$1} END {print k}' '$root/deploy/estate.conf')\" = web-blazor ]"
: > "$tmp/out"; "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --changed-files <(printf 'src/Gabinety.Web/Foo.cs\n') --github-output "$tmp/out" > "$tmp/table"
check "a change in src/Gabinety.Web selects web-blazor only, and it is in the build matrix" bash -c "grep -qx 'web-blazor=true' '$tmp/out' && ! grep -q '^web=' '$tmp/out' && grep -qx 'directory=false' '$tmp/out' && grep -q 'src/Gabinety.Web/Dockerfile' '$tmp/out'"
: > "$tmp/out"; "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head HEAD --changed-files <(printf 'Directory.Packages.props\n') --github-output "$tmp/out" > "$tmp/table"
check "a shared path selects web-blazor (shared = yes: it references ServiceDefaults)" grep -qx 'web-blazor=true' "$tmp/out"
check "secrets.map has the web host's row and no row for the retired Next.js app" bash -c "grep -Eq '^web-blazor +RateLimit__TrustedProxyKey ' '$root/deploy/secrets.map' && ! grep -Eq '^web +' '$root/deploy/secrets.map'"
check "deploy.yml has deploy-web-blazor gated on the detect output and no deploy-web job" bash -c "grep -q 'deploy-web-blazor:' '$root/.github/workflows/deploy.yml' && grep -q \"outputs.web-blazor == 'true'\" '$root/.github/workflows/deploy.yml' && ! grep -Eq '^  deploy-web:' '$root/.github/workflows/deploy.yml' && ! grep -q 'outputs.web ==' '$root/.github/workflows/deploy.yml'"
check "the old Fly config of the Next.js app is gone" bash -c "! test -e '$root/deploy/fly/web.fly.toml'"
check "_deploy-app.yml creates the web-blazor volume and fixes its owner" bash -c "grep -q 'webdata 1 fra' '$root/.github/workflows/_deploy-app.yml' && grep -q 'chown -R app /data' '$root/.github/workflows/_deploy-app.yml'"

# ---------- the web host reaches the private apps only over .internal / .flycast (F-03) ----------
check "private topology: web uses .internal/.flycast, no private app has [[services.ports]]" "$root/scripts/check-private-topology.sh"

# ---------- base-ref: a failed run must not hide an app from the next tag ----------
b="$tmp/brepo"; git init -q -b main "$b"
(cd "$b" && git config user.email t@t && git config user.name t && mkdir -p src/Gabinety.Web src/Gabinety.Booking && echo a > src/Gabinety.Web/a && echo a > src/Gabinety.Booking/a && git add -A && git commit -qm one && git tag v1 && git tag deployed/staging
  echo b >> src/Gabinety.Booking/a && git add -A && git commit -qm two && git tag v2          # run of v2 FAILED: the marker stays on v1
  echo b >> src/Gabinety.Web/a && git add -A && git commit -qm three && git tag v3)
check "base-ref is the marker when it exists" bash -c "cd '$b' && [ \"\$('$d/base-ref.sh' v3 2>/dev/null)\" = deployed/staging ]"
check "base-ref falls back to the previous release tag without a marker" bash -c "cd '$b' && git tag -d deployed/staging >/dev/null && [ \"\$('$d/base-ref.sh' v3 2>/dev/null)\" = v2 ]; git tag deployed/staging v1"
check "base-ref prints nothing on the first tag" bash -c "cd '$g' && [ -z \"\$('$d/base-ref.sh' v1 2>/dev/null)\" ]"
: > "$tmp/out"; (cd "$b" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head v3 --base "$("$d/base-ref.sh" v3 2>/dev/null)" --github-output "$tmp/out" > "$tmp/table")
check "after a failed previous run both its app (booking) and the new one (web-blazor) are selected" bash -c "$(declare -f sel nosel); tmp=$tmp; sel booking && sel web-blazor && nosel directory"
: > "$tmp/out"; (cd "$b" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head v3 --base v2 --github-output "$tmp/out" > "$tmp/table")
check "the old behaviour (previous tag as base) would have hidden booking" bash -c "$(declare -f sel nosel); tmp=$tmp; sel web-blazor && nosel booking"
(cd "$b" && git tag -f deployed/staging v3 >/dev/null)
: > "$tmp/out"; (cd "$b" && "$d/detect-changes.sh" --estate "$root/deploy/estate.conf" --existing-apps "$all" --head v3 --base "$("$d/base-ref.sh" v3 2>/dev/null)" --github-output "$tmp/out" > "$tmp/table")
check "after a successful run (marker moved) nothing is selected again" bash -c "$(declare -f nosel); tmp=$tmp; nosel booking && nosel web-blazor"

# ---------- image-ref: the registry switch ----------
check "ghcr.io, owner lower-cased" bash -c "[ \"\$('$d/image-ref.sh' ghcr.io KonradCinkusz booking gabinety-booking-staging)\" = ghcr.io/konradcinkusz/gabinety-booking ]"
check "registry.fly.io uses the Fly app" bash -c "[ \"\$('$d/image-ref.sh' registry.fly.io o booking gabinety-booking-staging)\" = registry.fly.io/gabinety-booking-staging ]"

# ---------- ensure-private-ip (flyctl stub) ----------
cat > "$tmp/fly" <<'STUB'
#!/usr/bin/env bash
echo "$*" >> "$STUB_LOG"
[ "$1 $2" = "ips list" ] && echo "${STUB_IPS:-[]}"
exit 0
STUB
: > "$tmp/log"; STUB_IPS='[{"Type":"v4"}]' "$d/ensure-private-ip.sh" gabinety-assistant-staging > /dev/null
check "a private IPv6 is allocated when missing" grep -qx 'ips allocate-v6 --private --app gabinety-assistant-staging' "$tmp/log"
: > "$tmp/log"; STUB_IPS='[{"Type":"private_v6"}]' "$d/ensure-private-ip.sh" gabinety-assistant-staging > /dev/null
check "an existing private IPv6 is kept" bash -c "! grep -q allocate '$tmp/log'"

# ---------- assert-no-public-ip (flyctl stub) ----------
: > "$tmp/log"; STUB_IPS='[{"Type":"private_v6"}]' "$d/assert-no-public-ip.sh" gabinety-assistant-staging > /dev/null 2>&1; rc=$?
check "an app with only a private IPv6 passes" bash -c "[ $rc = 0 ]"
STUB_IPS='[]' "$d/assert-no-public-ip.sh" gabinety-directory-staging > /dev/null 2>&1; rc=$?
check "an app with no address passes" bash -c "[ $rc = 0 ]"
for t in v4 shared_v4 v6; do STUB_IPS="[{\"Type\":\"$t\"}]" "$d/assert-no-public-ip.sh" gabinety-directory-staging > /dev/null 2>&1; rc=$?
  check "an app with a public $t address fails" bash -c "[ $rc = 1 ]"; done

# ---------- set-secrets ----------
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"s3cr3tpw","INTERNAL_KEY_BOOKING_TO_DIRECTORY":"dirkey","INTERNAL_KEY_REPUTATION_TO_DIRECTORY":"repdirkey","INTERNAL_KEY_DIRECTORY_TO_BOOKING":"dbkey","FLY_API_TOKEN":"tok","AUTH_JWT_PRIVATE_KEY_PEM":"-----BEGIN PRIVATE KEY-----\nLINE2\n-----END PRIVATE KEY-----"}'
: > "$tmp/log"; out="$("$d/set-secrets.sh" directory gabinety-directory-staging --map "$root/deploy/secrets.map" 2>&1)"; rc=$?
check "directory secrets are staged" bash -c "[ $rc = 0 ] && grep -q '^secrets set --stage --app gabinety-directory-staging ConnectionStrings__directorydb=Host=gabinety-postgres-staging.internal;.*Password=s3cr3tpw Internal__Callers__Booking=dirkey Internal__Callers__Reputation=repdirkey Internal__Keys__Booking=dbkey\$' '$tmp/log'"
check "values are masked" has "$out" "::add-mask::Host=gabinety-postgres-staging.internal;Port=5432;Database=directorydb;Username=postgres;Password=s3cr3tpw"
check "a value is never printed outside ::add-mask::" bash -c "! printf '%s\n' \"\$1\" | grep -v '^::add-mask::' | grep -q -e s3cr3tpw -e dirkey -e dbkey" _ "$out"
out="$("$d/set-secrets.sh" booking gabinety-booking-staging --map "$root/deploy/secrets.map" 2>&1)"; rc=$?
check "a missing required secret fails and names it, not its value" bash -c "[ $rc = 1 ]" && check "the failure names INTERNAL_KEY_REPUTATION_TO_BOOKING" has "$out" "INTERNAL_KEY_REPUTATION_TO_BOOKING"
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"pw","INTERNAL_KEY_REPUTATION_TO_BOOKING":"rb","INTERNAL_KEY_BOOKING_TO_DIRECTORY":"bd","INTERNAL_KEY_DIRECTORY_TO_BOOKING":"db","PAYMENTS_STRIPE_SECRET_KEY":"sk_test_x","PAYMENTS_STRIPE_WEBHOOK_SECRET":"whsec_x"}'
: > "$tmp/log"; out="$("$d/set-secrets.sh" booking gabinety-booking-staging --map "$root/deploy/secrets.map" 2>&1)"
check "an optional secret that is unset is skipped" has "$out" "skip Internal__Callers__Owner"
check "booking stages six secrets, the two Stripe ones among them" has "$out" "staging 6 secret(s)"
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"pw","INTERNAL_KEY_REPUTATION_TO_BOOKING":"rb","INTERNAL_KEY_BOOKING_TO_DIRECTORY":"bd","INTERNAL_KEY_DIRECTORY_TO_BOOKING":"db"}'
out="$("$d/set-secrets.sh" booking gabinety-booking-staging --map "$root/deploy/secrets.map" 2>&1)"; rc=$?
check "booking without the Stripe secrets fails the deploy and names them" bash -c "[ $rc = 1 ]" && check "the failure names PAYMENTS_STRIPE_SECRET_KEY" has "$out" "PAYMENTS_STRIPE_SECRET_KEY" && check "the failure names PAYMENTS_STRIPE_WEBHOOK_SECRET" has "$out" "PAYMENTS_STRIPE_WEBHOOK_SECRET"
check "no mapped secret is a single shared callee key" bash -c "! grep -E '^[a-z]+ +Internal__Key +' '$root/deploy/secrets.map'"
check "every INTERNAL_KEY_ secret of the map is used once per name pair (no key shared between callers)" bash -c "grep -oE '\$\{INTERNAL_KEY_[A-Z_]+\}' '$root/deploy/secrets.map' | sort | uniq -c | awk '\$1 > 2 {bad=1} END {exit bad}'"
export GH_SECRETS_JSON='{"POSTGRES_PASSWORD":"pw","AUTH_JWT_PRIVATE_KEY_PEM":"-----BEGIN X-----\nBODYLINE\n-----END X-----"}'
out="$("$d/set-secrets.sh" authservice gabinety-authservice-staging --map "$root/deploy/secrets.map" --dry-run 2>&1)"
check "every line of a multi-line secret is masked" has "$out" "::add-mask::BODYLINE"
check "dry run does not call flyctl" bash -c "! grep -q 'authservice' '$tmp/log' || [ \"\$(grep -c 'gabinety-authservice-staging' '$tmp/log')\" = 0 ]"
check "an app without rows has nothing to do" bash -c "'$d/set-secrets.sh' web gabinety-web-staging --map '$root/deploy/secrets.map' | grep -q 'no secrets for web'"

# ---------- e-mail and model rows (production configuration): all-or-nothing groups ----------
base='"POSTGRES_PASSWORD":"pw","INTERNAL_KEY_REPUTATION_TO_ASSISTANT":"ra","INTERNAL_KEY_REPUTATION_TO_BOOKING":"rb","INTERNAL_KEY_BOOKING_TO_DIRECTORY":"bd","INTERNAL_KEY_DIRECTORY_TO_BOOKING":"db","PAYMENTS_STRIPE_SECRET_KEY":"sk_test_x","PAYMENTS_STRIPE_WEBHOOK_SECRET":"whsec_x"'
stage() { GH_SECRETS_JSON="{$base${2:+,$2}}" "$d/set-secrets.sh" "$1" "gabinety-$1-staging" --map "$root/deploy/secrets.map" --dry-run 2>&1; }
out="$(stage booking '')"
check "no e-mail secrets: Notifications__Provider is NOT staged (the code default Log applies)" bash -c "! grep -q 'staging .*Notifications__Provider' <<< \"\$1\" && grep -q 'skip Notifications__Provider: gated' <<< \"\$1\"" _ "$out"
out="$(stage booking '"NOTIFICATIONS_SMTP_HOST":"smtp.demo.invalid"')"
check "a host without a sender does not switch the provider to Smtp" bash -c "! grep -E 'staging [0-9]+ secret.*Notifications__Provider' <<< \"\$1\"" _ "$out"
out="$(stage booking '"NOTIFICATIONS_SMTP_FROM":"x@demo.invalid"')"
check "a sender without a host does not switch the provider to Smtp" bash -c "! grep -E 'staging [0-9]+ secret.*Notifications__Provider' <<< \"\$1\"" _ "$out"
out="$(stage booking '"NOTIFICATIONS_SMTP_HOST":"smtp.demo.invalid","NOTIFICATIONS_SMTP_FROM":"x@demo.invalid","NOTIFICATIONS_SMTP_PORT":"465","NOTIFICATIONS_SMTP_TLS_MODE":"Implicit","NOTIFICATIONS_SMTP_USER":"u","NOTIFICATIONS_SMTP_PASSWORD":"demo-pw-Z9"')"
check "host and sender set: the provider is staged as Smtp with the whole group" bash -c "grep -Eq 'staging [0-9]+ secret.*Notifications__Provider.*Notifications__Smtp__Host.*Notifications__Smtp__FromAddress.*Notifications__Smtp__Port.*Notifications__Smtp__TlsMode.*Notifications__Smtp__User.*Notifications__Smtp__Password' <<< \"\$1\"" _ "$out"
check "the SMTP password is masked and never printed outside ::add-mask::" bash -c "grep -q '^::add-mask::demo-pw-Z9\$' <<< \"\$1\" && ! grep -v '^::add-mask::' <<< \"\$1\" | grep -q demo-pw-Z9" _ "$out"
check "the gate names are not part of the staged value" bash -c "grep -q '^::add-mask::Smtp\$' <<< \"\$1\"" _ "$out"
check "booking.fly.toml does not set Notifications__Provider (a staged secret and an [env] line must not compete)" bash -c "! grep -Eq '^[^#]*Notifications__Provider' '$root/deploy/fly/booking.fly.toml'"
check "no fly.toml of a deployed app sets a Fake or Mock provider" bash -c "! grep -Eiq '^[^#]*(Llm__Provider|Notifications__Provider) *= *\"(fake|mock)' '$root'/deploy/fly/*.fly.toml"
out="$(stage assistant '')"
check "no model secrets: no custom model entry and no Copilot token are staged (the Assistant stays 'unavailable')" bash -c "! grep -Eq 'staging [0-9]+ secret.*(Llm__CustomModels|Copilot__GitHubToken)' <<< \"\$1\"" _ "$out"
out="$(stage assistant '"LLM_CUSTOM_API_KEY":"demo-key-Q7"')"
check "an API key alone does not stage a half custom model (the host would refuse to start)" bash -c "! grep -Eq 'staging [0-9]+ secret.*Llm__CustomModels' <<< \"\$1\"" _ "$out"
out="$(stage assistant '"LLM_CUSTOM_ENDPOINT":"https://llm.demo.invalid/v1","LLM_CUSTOM_MODEL":"demo-model","LLM_CUSTOM_API_KEY":"demo-key-Q7","COPILOT_GITHUB_TOKEN":"demo-tok"')"
check "endpoint and model set: Id, Model, BaseUrl and ApiKey of entry 0 are staged, with the Copilot token" bash -c "grep -Eq 'staging [0-9]+ secret.*Llm__CustomModels__0__Id.*Llm__CustomModels__0__Model.*Llm__CustomModels__0__BaseUrl.*Llm__CustomModels__0__ApiKey.*Copilot__GitHubToken' <<< \"\$1\"" _ "$out"
check "the model API key is masked" bash -c "grep -q '^::add-mask::demo-key-Q7\$' <<< \"\$1\" && ! grep -v '^::add-mask::' <<< \"\$1\" | grep -q demo-key-Q7" _ "$out"
check "every Llm__ and Notifications__ name of secrets.map is a key the code binds (config-keys.map)" bash -c "
  for n in \$(awk '!/^#/ && NF && (\$2 ~ /^Llm__|^Notifications__|^Copilot__/) {print \$2}' '$root/deploy/secrets.map'); do
    k=\${n//__/:}; k=\$(sed -E 's/:[0-9]+:/:0:/' <<< \"\$k\"); grep -Eq \"^(booking|assistant) +\$k +\" '$root/deploy/config-keys.map' || { echo \"\$n not in config-keys.map\"; exit 1; }; done"

# ---------- config coverage: every required key of a deployed app has a source (scripts/check-config-coverage.sh) ----------
cc="$root/scripts/check-config-coverage.sh"
check "config coverage passes on the repository" "$cc"
mkdir -p "$tmp/cc/scripts"; cp -r "$root/deploy" "$tmp/cc/deploy"; ln -s "$root/src" "$tmp/cc/src"
check "config coverage: a required key without a source fails and names it" bash -c "
  sed -i '/^ *Site__BaseUrl/d' '$tmp/cc/deploy/fly/booking.fly.toml'; out=\$('$cc' --root '$tmp/cc' 2>&1); rc=\$?; [ \$rc = 1 ] && grep -q 'booking: required key Site:BaseUrl has no source' <<< \"\$out\""
rm -rf "$tmp/cc/deploy"; cp -r "$root/deploy" "$tmp/cc/deploy"
check "config coverage: a key the code reads that nobody classified fails" bash -c "
  sed -i '/^booking  *Site:BaseUrl /d' '$tmp/cc/deploy/config-keys.map'; out=\$('$cc' --root '$tmp/cc' 2>&1); rc=\$?; [ \$rc = 1 ] && grep -q 'booking: the code reads Site:BaseUrl but' <<< \"\$out\""
rm -rf "$tmp/cc/deploy"; cp -r "$root/deploy" "$tmp/cc/deploy"
check "config coverage: a required secret row removed from secrets.map fails (Stripe key)" bash -c "
  sed -i '/^booking  *Payments__Stripe__SecretKey/d' '$tmp/cc/deploy/secrets.map'; out=\$('$cc' --root '$tmp/cc' 2>&1); rc=\$?; [ \$rc = 1 ] && grep -q 'Payments:Stripe:SecretKey has no source' <<< \"\$out\""
rm -rf "$tmp/cc/deploy"; cp -r "$root/deploy" "$tmp/cc/deploy"
check "config coverage: a fake-only key set in a deployed toml fails" bash -c "
  sed -i 's/^\\[env\\]/[env]\n  GABINETY_FAKE = \"1\"/' '$tmp/cc/deploy/fly/booking.fly.toml'; out=\$('$cc' --root '$tmp/cc' 2>&1); rc=\$?; [ \$rc = 1 ] && grep -q 'GABINETY_FAKE' <<< \"\$out\""

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
check "smoke public calls the public URL and path" bash -c "PATH='$tmp/bin':\$PATH '$d/smoke.sh' web-blazor gabinety-web-blazor-staging public /alive | grep -q 'SMOKE web-blazor ok'; grep -q 'https://gabinety-web-blazor-staging.fly.dev/alive' '$tmp/log'"
check "smoke public honours PUBLIC_URL" bash -c "PATH='$tmp/bin':\$PATH PUBLIC_URL=https://example.test/ '$d/smoke.sh' web-blazor gabinety-web-blazor-staging public /alive >/dev/null; grep -q 'https://example.test/alive' '$tmp/log'"
check "smoke public fails when curl fails" bash -c "! PATH='$tmp/bin':\$PATH STUB_CURL_OK=0 '$d/smoke.sh' web-blazor gabinety-web-blazor-staging public /alive >/dev/null"

# ---------- verify-default-branch ----------
r="$tmp/vrepo"; git init -q -b main "$r"; (cd "$r" && git config user.email t@t && git config user.name t && git commit -q --allow-empty -m a && git update-ref refs/remotes/origin/main HEAD && git checkout -q -b side && git commit -q --allow-empty -m b)
main_sha="$(git -C "$r" rev-parse main)"; side_sha="$(git -C "$r" rev-parse side)"
check "a commit on the default branch passes" bash -c "cd '$r' && '$d/verify-default-branch.sh' $main_sha main >/dev/null"
check "a commit off the default branch fails" bash -c "cd '$r' && ! '$d/verify-default-branch.sh' $side_sha main >/dev/null 2>&1"
check "an unfetched default branch fails" bash -c "cd '$r' && ! '$d/verify-default-branch.sh' $main_sha nope >/dev/null 2>&1"

# ---------- summary ----------
check "summary passes when all succeeded or were skipped" bash -c "NEEDS_JSON='{\"deploy-web-blazor\":{\"result\":\"success\"},\"deploy-booking\":{\"result\":\"skipped\"}}' '$d/summary.sh' v1.0.0 | grep -q 'not selected'"
check "summary fails on a failed job" bash -c "! NEEDS_JSON='{\"deploy-web-blazor\":{\"result\":\"failure\"}}' '$d/summary.sh' v1.0.0 >/dev/null"

echo; echo "$pass passed, $fail failed"; [ "$fail" = 0 ]
