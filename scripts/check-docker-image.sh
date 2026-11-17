#!/usr/bin/env bash
# Proves one backend image (T-09.02): runs as non-root, answers /alive, resolves Europe/Warsaw.
# Usage: scripts/check-docker-image.sh <image> [--copilot-runtime]
set -euo pipefail
image="${1:?image}"; port="${PORT:-18080}"; name="chk-$$"
trap 'docker logs "$name" 2>&1 | tail -40 || true; docker rm -f "$name" >/dev/null 2>&1 || true' EXIT
# Fake-profile configuration (no credentials, no database): the same values the AppHost fake profile sets.
docker run -d --name "$name" -p "$port:8080" -e GABINETY_FAKE=1 -e Database__Provider=InMemory -e Llm__Provider=Fake \
  -e Payments__Provider=Mock -e Notifications__Provider=Log -e Identity__Authority=http://localhost:5100 "$image" >/dev/null
uid=$(docker exec "$name" id -u); echo "uid=$uid"; [ "$uid" != 0 ] || { echo "FAIL runs as root"; exit 1; }
docker exec "$name" test -e /usr/share/zoneinfo/Europe/Warsaw && echo "ok   Europe/Warsaw zoneinfo present"
for i in $(seq 1 60); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' -m 3 "http://localhost:$port/alive" || true)" = 200 ] && { echo "ok   /alive 200"; break; }
  [ "$i" = 60 ] && { echo "FAIL /alive did not answer 200"; exit 1; }; sleep 1
done
for i in $(seq 1 30); do
  [ "$(docker inspect -f '{{.State.Health.Status}}' "$name")" = healthy ] && { echo "ok   HEALTHCHECK healthy"; break; }
  [ "$i" = 30 ] && { echo "FAIL HEALTHCHECK not healthy"; exit 1; }; sleep 2
done
if [ "${2:-}" = "--copilot-runtime" ]; then
  found=$(docker exec "$name" bash -c "find /app -iname '*copilot*' ! -name '*.dll' ! -name '*.pdb' ! -name '*.xml' ! -name '*.json' | head -5")
  [ -n "$found" ] && echo "ok   Copilot runtime present: $found" || { echo "FAIL no Copilot runtime in /app (A-05)"; exit 1; }
fi
