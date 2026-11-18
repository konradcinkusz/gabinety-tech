#!/usr/bin/env bash
# T-09.03: a deploy tag must point at a commit that is on the default branch. Exit 0 = yes, 1 = no (or the branch cannot be read).
# Usage: scripts/deploy/verify-default-branch.sh <commit> <default-branch> [remote]     (remote defaults to origin; its branch must be fetched)
set -euo pipefail
sha="${1:?usage: verify-default-branch.sh <commit> <default-branch> [remote]}"
branch="${2:?usage: verify-default-branch.sh <commit> <default-branch> [remote]}"
remote="${3:-origin}"
tip="$(git rev-parse --verify --quiet "refs/remotes/$remote/$branch^{commit}")" || { echo "FAIL: $remote/$branch is not fetched" >&2; exit 1; }
if git merge-base --is-ancestor "$sha" "$tip"; then
  echo "ok   $sha is reachable from $remote/$branch"
  git branch -r --contains "$sha" | sed 's/^ */     contained in: /'
else
  echo "FAIL: $sha is not on $remote/$branch; tag a commit of the default branch" >&2
  exit 1
fi
