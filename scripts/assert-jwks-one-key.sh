#!/usr/bin/env bash
# Deploy assertion (T-09.04): the JWKS of an authservice has exactly one key. An empty key set (HS256 or a missing private key) is NOT healthy.
# Usage: scripts/assert-jwks-one-key.sh <authservice-base-url>      exit 0 = exactly one key, 1 = otherwise
set -euo pipefail
base="${1:?usage: assert-jwks-one-key.sh <base-url>}"
body="$(curl -fsS -m 10 --retry 3 --retry-delay 2 "${base%/}/.well-known/jwks.json")" || { echo "FAIL: JWKS not reachable at ${base%/}/.well-known/jwks.json" >&2; exit 1; }
n="$(printf '%s' "$body" | python3 -I -c 'import json,sys; print(len(json.load(sys.stdin).get("keys", [])))')" || { echo "FAIL: JWKS is not JSON" >&2; exit 1; }
if [ "$n" = 1 ]; then echo "ok   JWKS has exactly 1 key"; else echo "FAIL: JWKS has $n keys, expected exactly 1 (is Jwt__PrivateKeyPem set and Jwt__Algorithm=RS256?)" >&2; exit 1; fi
