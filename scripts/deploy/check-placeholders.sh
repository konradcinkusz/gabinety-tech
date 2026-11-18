#!/usr/bin/env bash
# T-09.03: refuse to deploy a fly.toml that still has an owner placeholder like <authservice-public-host> (docs/operations/AUTH.md section 5).
# Only non-comment lines count. Usage: check-placeholders.sh <fly.toml>...
set -euo pipefail
bad=0
for f in "$@"; do
  [ -f "$f" ] || { echo "FAIL: $f does not exist" >&2; bad=1; continue; }
  if hits="$(grep -nE '^[^#]*<[A-Za-z][A-Za-z0-9_-]*>' "$f")"; then
    echo "FAIL: $f still has placeholders (the owner fills them, docs/operations/DEPLOY.md):" >&2
    printf '%s\n' "$hits" | sed 's/^/  /' >&2; bad=1
  fi
done
exit "$bad"
