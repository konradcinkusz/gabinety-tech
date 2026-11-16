#!/usr/bin/env bash
# Every .csproj in the repository must be in the solution (SPEC 9 guard).
set -u
cd "$(dirname "$0")/.."
missing=0
for p in $(find src tools tests -name '*.csproj' -not -path '*/obj/*' | sort); do
  grep -q "$(basename "$p")" GabinetyTech.sln || { echo "not in solution: $p"; missing=1; }
done
[ $missing = 0 ] && echo "check-solution-projects: ok"
exit $missing
