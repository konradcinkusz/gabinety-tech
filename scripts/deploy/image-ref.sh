#!/usr/bin/env bash
# T-09.03: the image repository (no tag) of a built app. The registry is ONE switch, the variable IMAGE_REGISTRY (default ghcr.io).
#   ghcr.io          ghcr.io/<owner>/gabinety-<key>      (owner lower-cased, as GHCR requires)
#   registry.fly.io  registry.fly.io/<fly-app>            (private to the Fly organisation; the app must exist before the push)
#   anything else    <registry>/<owner>/gabinety-<key>    (login not supported by the workflow)
# Usage: image-ref.sh <registry> <owner> <key> <fly-app>
set -euo pipefail
registry="${1:?usage: image-ref.sh <registry> <owner> <key> <fly-app>}"; owner="${2:?}"; key="${3:?}"; fly="${4:?}"
owner="$(printf '%s' "$owner" | tr '[:upper:]' '[:lower:]')"
case "$registry" in
  registry.fly.io) echo "registry.fly.io/$fly" ;;
  *) echo "$registry/$owner/gabinety-$key" ;;
esac
