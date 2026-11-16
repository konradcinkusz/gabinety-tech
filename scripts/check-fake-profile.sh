#!/usr/bin/env bash
# Waits for every service of the fake profile to answer /health 200 (SPEC 4.1, T-00.03). Exit 1 on timeout.
set -u
LIMIT="${LIMIT:-180}"
declare -A URLS=(
  [devidentity]=http://localhost:5100/health [directory]=http://localhost:5101/health
  [booking]=http://localhost:5102/health [reputation]=http://localhost:5103/health
  [assistant]=http://localhost:5104/health [web]=http://localhost:3000/api/health
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
exit $fail
