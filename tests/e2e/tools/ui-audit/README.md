# ui-audit

Visits every route of the Blazor host (`src/Gabinety.Web`) as visitor, client, practitioner and administrator at 1440x900 and 390x844, light and dark, drives the booking, review,
onboarding and dispute-resolution flows through the UI, records mechanical findings and writes screenshots. Result: `docs/ui/audit/findings.json` and
`docs/ui/audit/<role>/<route>-<viewport>-<scheme>.png`; the reading of it is `docs/ui/UI-AUDIT.md`.

**It writes to the system** (registers a user, books, pays, cancels, reviews). Run it only against a throw-away fake profile (`docs/GETTING-STARTED.md` section 1), after the seeder has finished.

```bash
pnpm --dir tests/e2e install --frozen-lockfile
PW_CHROMIUM_PATH=/opt/pw-browsers/chromium node tests/e2e/tools/ui-audit/audit.mjs      # about 6 minutes; AUDIT_ONLY=flows | visitor | client | practitioner | admin
python3 -I tests/e2e/tools/ui-audit/compress.py docs/ui/audit                           # palette PNG, each file under 150 KB (needs Pillow)
python3 tests/e2e/tools/ui-audit/summarize.py docs/ui/audit/findings.json               # one line per page visit
python3 tests/e2e/tools/ui-audit/contrast.py                                            # contrast table of src/Gabinety.Web/wwwroot/tokens.css (exit 1 on a failing pair)
```

Env: `E2E_WEB_URL` (default `http://localhost:3000`), `AUDIT_OUT` (default `docs/ui/audit`), `PW_CHROMIUM_PATH`.
The inert-button probe clicks buttons, so it runs only on public read-only pages (it once disabled a practitioner's offer; see UI-AUDIT.md section 7). Do not widen it.

The read-only subset runs in CI as `tests/e2e/journeys/ui-gate.spec.ts` (every route at 390 px, both schemes: no horizontal scroll, no serious or critical axe violation). The audit itself writes, so it is run by hand on a throw-away stack; sign-in is the host's form post (`harness/auth.ts` `signInViaFormPost`).
