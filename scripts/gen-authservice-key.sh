#!/usr/bin/env bash
# Generates the authservice RS256 signing key (PKCS#8 PEM, RSA 2048) for Linux (T-09.04). The private key is a SECRET.
# Usage: scripts/gen-authservice-key.sh <out-file>                 write the key, mode 0600, refuse to overwrite
#        scripts/gen-authservice-key.sh <out-file> --fly <app>      also set it as the secret Jwt__PrivateKeyPem on that Fly app
# Nothing is printed except the file name and the key's SHA-256 thumbprint of its public part (safe to log).
set -euo pipefail
out="${1:?usage: gen-authservice-key.sh <out-file> [--fly <app>]}"
[ -e "$out" ] && { echo "refusing to overwrite $out" >&2; exit 1; }
command -v openssl >/dev/null || { echo "openssl is required" >&2; exit 1; }
dir="$(cd "$(dirname "$out")" && pwd)"
if git -C "$dir" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  echo "refusing: $out is inside a git work tree; write it outside the repository" >&2; exit 1
fi
umask 077
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$out" 2>/dev/null
chmod 600 "$out"
echo "wrote $out"
echo "public key sha256: $(openssl pkey -in "$out" -pubout -outform DER | openssl dgst -sha256 | awk '{print $NF}')"
if [ "${2:-}" = "--fly" ]; then
  app="${3:?--fly needs an app name}"
  command -v fly >/dev/null || { echo "fly CLI not found; set it yourself: fly secrets set --app $app Jwt__PrivateKeyPem=\"\$(cat $out)\"" >&2; exit 1; }
  fly secrets set --app "$app" "Jwt__PrivateKeyPem=$(cat "$out")" --stage
  echo "staged Jwt__PrivateKeyPem on $app (deploy applies it)"
fi
