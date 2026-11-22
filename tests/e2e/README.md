# tests/e2e: Playwright harness and journeys

The harness (T-08.03a) runs Playwright against a **running fake profile**. It starts nothing itself. Journeys J1-J7 (T-08.03 to T-08.05) only add spec files under `journeys/`; `playwright.config.ts`, `harness/**` and `package.json` belong to the harness task.

## Run it

```
dotnet run --project src/Gabinety.AppHost --launch-profile fake     # terminal 1 (CI starts it the same way)
pnpm --dir tests/e2e install && pnpm --dir tests/e2e exec playwright install chromium   # once
pnpm --dir tests/e2e test                                           # terminal 2; add -- --grep "J1|J2" to pick journeys
```

`globalSetup` waits (up to 4 min, `E2E_WAIT_SECONDS`) for `/health` of DevIdentity, Directory, Booking, Reputation, Assistant and the web app, then for the seeded practitioners, then until the demo seeder has **finished** (no seeder process, both test clocks at 0, held for 10 s; up to 8 min, `E2E_SEEDER_WAIT_SECONDS`; `E2E_WAIT_FOR_SEEDER=0` skips it for an already seeded profile), because the seeder moves the global test clock while it runs. Chromium at a fixed path (sandbox, some CI images): `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`. Other overrides: `E2E_WEB_URL`, `E2E_DIRECTORY_URL`, ... (`harness/env.ts`). Traces and screenshots are kept on failure only.

## Which host (Next.js or Blazor)

`harness/host.ts` is the one place that reads `HOST=next|blazor` (default `next`: nothing changes when unset) and `BASE_URL` (alias `E2E_WEB_URL`; default `http://localhost:3000` for next, `http://localhost:3001` for blazor). The web health path (`/api/health` vs `/health`), the sign-in helper (`signInViaBff` posts the real `/logowanie` form with its antiforgery token when `HOST=blazor`; the Blazor host has no `/api/auth/login`) and `harness/circuit.ts` (`watchCircuit` / `gotoInteractive`: wait for the circuit before typing into an interactive page) hang off it.

- `HOST=next` runs everything except `*.blazor.spec.ts`.
- `HOST=blazor` runs ONLY the specs listed in `BLAZOR_SPECS` (`harness/host.ts`): today `journeys/j7-foreign-origin.blazor.spec.ts`. Each port session adds its journey to that list once its pages exist.

CI command for the Blazor variant (the workflow is owned by the CI/E2E wiring; same running fake profile, the AppHost starts `web-blazor` on 3001):

```
HOST=blazor pnpm --dir tests/e2e test          # or: HOST=blazor BASE_URL=http://localhost:3001 ...
```

## What the harness gives you

Import `test` and `expect` from `../harness/fixtures`, never from `@playwright/test`.

| Need | Use |
|---|---|
| Be a seeded demo user | `await signInAs("demo-client")` (BFF, fast) or `signInAs.viaForm(page, name)` (real form). Users: `demo-client`, `demo-admin`, `demo-practitioner-1..8`, one fixed development password (`harness/env.ts`, not a secret) |
| Pass business time (24 h, 48 h, 14 days) | `clock.advanceHours(49)`, `clock.advanceDays(15)`. Moves the test clock of Booking and Reputation (CONTRACTS 10.4); restored to 0 after every test by the fixture. **Never `waitForTimeout` for business time.** |
| Clean up after a journey that wrote data | `test.afterAll(...)` calling `reseedIfRequested()` from `harness/booking-api.ts` (it runs `resetAndReseed()` from `harness/demo-data.ts`, SPEC 7) **only when `process.env.E2E_RESEED === "1"`** (default off: CI's stack is ephemeral and each reseed costs about 6 minutes of the job's 25). Set `E2E_RESEED=1` for a local re-run on a long-lived profile. Journeys must not depend on leftovers: unique `demo-` ids, their own data. Once per journey, not per test |
| Call a service directly | `fetchRetrying(url)` from `harness/http.ts`: waits and retries on 429 and 503. The services limit requests per client address (60 a minute by default; the AppHost fake profile raises it to 6000 with `RateLimit__PerMinute`) and everything here is `localhost`, so a 429 can still happen against a profile started another way |
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
import { restoreClock } from "../harness/clock";
import { reseedIfRequested } from "../harness/booking-api";

test.describe("J3 book and review", () => {
  test.afterAll(async () => {
    test.setTimeout(900_000); // a reseed takes minutes; the default hook timeout is 60 s
    await restoreClock();
    await reseedIfRequested(); // does nothing unless E2E_RESEED=1
  });

  test("client books, time passes, booking completes", async ({ page, signInAs, clock }) => {
    await signInAs("demo-client");
    // ... drive the UI, assert what is visible ...
    await clock.advanceHours(50);
    // ... assert the follow-up state ...
  });
});
```

`harness/smoke.spec.ts` is the working example (home page, the 6 seeded practitioners, clock, sign-in).
