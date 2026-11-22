#!/usr/bin/env bash
# Waits for every service of the fake profile to answer /health 200 (SPEC 4.1, T-00.03), then for the demo seeder's dataset (T-08.02): the Directory
# lists at least WANT_PRACTITIONERS (default 6, the approved demo practitioners) and the web app's /programisci answers 200 (the Blazor host). Exit 1 on timeout.
set -u
LIMIT="${LIMIT:-180}"
declare -A URLS=(
  [devidentity]=http://localhost:5100/health [directory]=http://localhost:5101/health
  [booking]=http://localhost:5102/health [reputation]=http://localhost:5103/health
  [assistant]=http://localhost:5104/health [web]=http://localhost:3000/health
)
fail=0
for name in devidentity directory booking reputation assistant web; do
  ok=0
  for ((i = 0; i < LIMIT; i++)); do
    code=$(curl -s -o /dev/null -w '%{http_code}' -m 3 "${URLS[$name]}" || true)
    if [ "$code" = "200" ]; then ok=1; break; fi
    sleep 1
  done
  if [ $ok = 1 ]; then echo "ok   $name ${URLS[$name]}"; else echo "FAIL $name ${URLS[$name]} (last status: $code)"; fail=1; fi
done

# the seeded dataset: the seeder starts after the services are healthy and takes a while (Booking waits for the auto-complete job)
WANT="${WANT_PRACTITIONERS:-6}"
found=0
for ((i = 0; i < LIMIT; i++)); do
  found=$(curl -s -m 5 "http://localhost:5101/api/v1/practitioners?limit=50" | grep -o '"slug":"demo-' | wc -l || true)
  if [ "$found" -ge "$WANT" ]; then break; fi
  sleep 1
done
if [ "$found" -ge "$WANT" ]; then echo "ok   seeded practitioners: $found (wanted $WANT)"; else echo "FAIL seeded practitioners: $found (wanted $WANT)"; fail=1; fi
# The seeder, Booking and the web host all call the Directory from this machine, and the Directory's 60-per-minute limit is per client address (CONTRACTS 10.2), so while the
# seeder is busy the web host's list page can answer 500 (it reports the Directory's 429 as directory_unavailable). Retry until the seeding has calmed down.
code=000
for ((i = 0; i < LIMIT; i++)); do
  code=$(curl -s -o /dev/null -w '%{http_code}' -m 10 http://localhost:3000/programisci || true)
  if [ "$code" = "200" ]; then break; fi
  sleep 1
done
if [ "$code" = "200" ]; then echo "ok   web /programisci"; else echo "FAIL web /programisci (last status: $code)"; fail=1; fi
exit $fail
