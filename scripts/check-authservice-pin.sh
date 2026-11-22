#!/usr/bin/env bash
# One authservice image, pinned by digest, everywhere it is named: the Fly config, the AppHost default profile, the integration tests and the CI integration job.
# A bump changes all of them in one commit (docs/spikes/authservice.md section 4); this fails when one is left behind. Run from anywhere.
set -euo pipefail
cd "$(dirname "$0")/.."
want="$(grep -oE 'authservice@sha256:[0-9a-f]{64}' deploy/fly/authservice.fly.toml | head -1 | sed 's/.*sha256://')"
[ -n "$want" ] || { echo "FAIL: no digest in deploy/fly/authservice.fly.toml" >&2; exit 1; }
status=0
for f in src/Gabinety.AppHost/Program.cs tests/Gabinety.Integration.Tests/ThinSlice/ThinSliceTests.cs tests/Gabinety.Integration.Tests/RealAuth/RealAuthFixture.cs .github/workflows/ci.yml; do
  found="$(grep -oE '[0-9a-f]{64}' "$f" | sort -u | tr '\n' ' ')"
  if [ "${found% }" = "$want" ]; then echo "ok   $f"; else echo "FAIL $f names '${found% }', the fly toml pins $want" >&2; status=1; fi
done
exit $status
