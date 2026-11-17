# tests/e2e: Playwright harness and journeys

The harness (T-08.03a) runs Playwright against a **running fake profile**. It starts nothing itself. Journeys J1-J7 (T-08.03 to T-08.05) only add spec files under `journeys/`; `playwright.config.ts`, `harness/**` and `package.json` belong to the harness task.

## Run it

```
dotnet run --project src/Gabinety.AppHost --launch-profile fake     # terminal 1 (CI starts it the same way)
pnpm --dir tests/e2e install && pnpm --dir tests/e2e exec playwright install chromium   # once
pnpm --dir tests/e2e test                                           # terminal 2; add -- --grep "J1|J2" to pick journeys
```

`globalSetup` waits (up to 4 min, `E2E_WAIT_SECONDS`) for `/health` of DevIdentity, Directory, Booking, Reputation, Assistant and the web app, then for the seeded practitioners, then until the demo seeder has **finished** (no seeder process, both test clocks at 0, held for 10 s; up to 8 min, `E2E_SEEDER_WAIT_SECONDS`; `E2E_WAIT_FOR_SEEDER=0` skips it for an already seeded profile), because the seeder moves the global test clock while it runs. Chromium at a fixed path (sandbox, some CI images): `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`. Other overrides: `E2E_WEB_URL`, `E2E_DIRECTORY_URL`, ... (`harness/env.ts`). Traces and screenshots are kept on failure only.

## What the harness gives you

Import `test` and `expect` from `../harness/fixtures`, never from `@playwright/test`.

| Need | Use |
|---|---|
| Be a seeded demo user | `await signInAs("demo-client")` (BFF, fast) or `signInAs.viaForm(page, name)` (real form). Users: `demo-client`, `demo-admin`, `demo-practitioner-1..8`, one fixed development password (`harness/env.ts`, not a secret) |
| Pass business time (24 h, 48 h, 14 days) | `clock.advanceHours(49)`, `clock.advanceDays(15)`. Moves the test clock of Booking and Reputation (CONTRACTS 10.4); restored to 0 after every test by the fixture. **Never `waitForTimeout` for business time.** |
| Clean up after a journey that wrote data | `test.afterAll(...)` calling `demoData.resetAndReseed()` (SPEC 7) **only when `process.env.E2E_RESEED === "1"`** (default off: CI's stack is ephemeral and each reseed costs about 6 minutes of the job's 25). Set `E2E_RESEED=1` for a local re-run on a long-lived profile. Journeys must not depend on leftovers: unique `demo-` ids, their own data. Once per journey, not per test |
| Call a service directly | `fetchRetrying(url)` from `harness/http.ts`: waits and retries on 429 and 503. The Directory limits **60 requests a minute per client address** and everything here is `localhost`, so a 429 is normal |
| Page objects | `harness/pages.ts`; add yours there, not selectors in specs |
| Accessibility | `await expectNoSeriousA11yViolations(page)` on every page a journey visits (T-08.03) |

## Writing a journey

1. File `journeys/j3-book-and-review.spec.ts`; `test.describe("J3 ...")` so `--grep "J3"` selects it.
2. Assert **outcomes and follow-up state** (what the user sees, then the API's view: booking status, payment state), not that a click happened.
3. Locators: role and label first (`getByRole("button", { name: "Zaloguj się" })`, `getByLabel`), then text; `data-testid` only where no accessible name exists, and say why in a comment. No CSS chains, no `nth` on unstable lists.
4. Wait on state (`expect(...).toBeVisible()`, `expect.poll`), never on fixed sleeps.
5. Fixtures are synthetic (`demo-` namespace). No secrets. Do not log user free text.
6. Journeys run serially in one worker: they share one in-memory system. If you changed the clock the fixture restores it; if you wrote rows, reset in `afterAll`.

A skeleton:

```ts
import { test, expect } from "../harness/fixtures";

test.describe("J3 book and review", () => {
  test.afterAll(async ({ demoData }) => demoData.resetAndReseed());

  test("client books, time passes, booking completes", async ({ page, signInAs, clock }) => {
    await signInAs("demo-client");
    // ... drive the UI, assert what is visible ...
    await clock.advanceHours(50);
    // ... assert the follow-up state ...
  });
});
```

`harness/smoke.spec.ts` is the working example (home page, the 6 seeded practitioners, clock, sign-in).
