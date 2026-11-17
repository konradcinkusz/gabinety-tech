# Evaluations of the two Assistant prompts (T-06.04)

Scenario files (synthetic, `demo-`; no real person, e-mail or phone):

- `brief.v1.scenarios.json`: 30 Polish/English briefs (every problem category, vague, English, personal data, prompt injection, empty-ish, very long).
- `triage.v1.scenarios.json`: 20 reviews (clean, spam, abusive, contact details, off-topic).

Fields: `id`, `tags`, `text` (`repeat` repeats it, capped at 2000 characters), `rating` (triage), `forbidden` (strings that must never appear in a suggestion),
`expect` (the author's intent) and `deterministic` (the fake gateway reproduces that intent, COPILOT-SDK 10, so CI gates on it; against a live model every intent is only reported).

The runner is `tests/Gabinety.Assistant.Tests/Evals/`. It drives the real brief and triage services (prompt, validator, repair attempt) and asserts:
`schema_valid`, `slugs_in_taxonomy`, `length_limits`, `no_echo` (brief); `schema_valid`, `reasons_in_closed_set`, `flag_consistent` (triage); `intent` (both).

- CI and `dotnet test --filter "tier!=integration"`: fake gateway, `Evals.*` tests.
- Live (owner's machine only): `EVAL_LIVE=1 dotnet test tests/Gabinety.Assistant.Tests --filter "tier=live"` against the configured gateway
  (`Llm__Provider`, `Llm__CustomModels__*`, a Copilot login, `Assistant__Model`). It spends credits. It is refused when `CI` is set and when `Llm:Provider` is Fake.
  Reports go to `evals/reports/` (ignored by git): rates per assertion, latency p95, tokens, credits, failures as scenario ids. No scenario or reply text.
