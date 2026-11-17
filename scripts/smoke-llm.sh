#!/usr/bin/env bash
# Owner-run live smoke of the Assistant (T-02.05, COPILOT-SDK 11): runs the brief feature once on a fixed synthetic brief and prints
# model id, latency, tokens and AI credits. Refuses (exit 2) when CI is set, when Llm:Provider would be Fake, or without dotnet.
# It SPENDS one real call: AI credits of the Copilot model on this machine, or the API-key model's tokens (the Assistant's own daily budget is not used).
# Choose the model with Assistant__Model; use a Copilot login or an Llm__CustomModels__* entry, as for the service itself.
set -u

if [ -n "${CI:-}" ]; then
  echo "smoke-llm: refusing to run when CI is set (a live call spends real credits)." >&2
  exit 2
fi

provider="${Llm__Provider:-${LLM__PROVIDER:-Copilot}}"
if [ "${provider,,}" = "fake" ]; then
  echo "smoke-llm: Llm:Provider is Fake; a smoke run needs a real model." >&2
  exit 2
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "smoke-llm: dotnet is required (see global.json for the SDK version)." >&2
  exit 2
fi

echo "smoke-llm: this makes ONE real model call and spends AI credits (Copilot) or API tokens (your own key)." >&2
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet run --project "$root/src/Gabinety.Assistant" -- smoke "$@"
