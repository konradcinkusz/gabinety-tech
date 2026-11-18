#!/usr/bin/env bash
# T-09.03: the commit to diff against. The marker tag `deployed/staging` is moved only after a whole run succeeded (deploy.yml, job mark-deployed), so a failed
# run can never hide an app from the next tag. With no marker (first deploys) the previous release tag is the fallback; with neither, nothing is printed
# and every app is selected. Usage: base-ref.sh <head-commit> [marker-tag]      Prints the ref; the reason goes to stderr.
set -euo pipefail
head="${1:?usage: base-ref.sh <head-commit> [marker-tag]}"; marker="${2:-deployed/staging}"
if git rev-parse --quiet --verify "refs/tags/$marker^{commit}" > /dev/null; then
  echo "base: marker $marker (last fully successful deploy)" >&2; echo "$marker"
elif prev="$(git describe --tags --abbrev=0 --match 'v*' "$head^" 2> /dev/null)"; then
  echo "base: no $marker yet, falling back to the previous release tag $prev" >&2; echo "$prev"
else
  echo "base: none (first deploy), every app is selected" >&2
fi
