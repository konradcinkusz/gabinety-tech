# tests/e2e: Playwright harness and journeys

The harness (T-08.03a) runs Playwright against a **running fake profile**. It starts nothing itself. Journeys J1-J7 (T-08.03 to T-08.05) only add spec files under `journeys/`; `playwright.config.ts`, `harness/**` and `package.json` belong to the harness task.

## Run it

```
dotnet run --project src/Gabinety.AppHost --launch-profile fake     # terminal 1 (CI starts it the same way)
pnpm --dir tests/e2e install && pnpm --dir tests/e2e exec playwright install chromium   # once
pnpm --dir tests/e2e test                                           # terminal 2; add -- --grep "J1|J2" to pick journeys
```

`globalSetup` waits (up to 4 min, `E2E_WAIT_SECONDS`) for `/health` of DevIdentity, Directory, Booking, Reputation, Assistant and the web app, then for the seeded practitioners, then until the demo seeder has **finished** (no seeder process, both test clocks at 0, held for 10 s; up to 8 min, `E2E_SEEDER_WAIT_SECONDS`; `E2E_WAIT_FOR_SEEDER=0` skips it for an already seeded profile), because the seeder moves the global test clock while it runs. Chromium at a fixed path (sandbox, some CI images): `PW_CHROMIUM_PATH=/opt/pw-browsers/chromium`. Other overrides: `E2E_WEB_URL`, `E2E_DIRECTORY_URL`, ... (`harness/env.ts`). Traces and screenshots are kept on failure only.

## Two web hosts (ADR-0010)

The same journeys run against the Next.js app and the Blazor Web App. `harness/host.ts` is the one place that reads `HOST=next|blazor` (default `next`) and `BASE_URL` (alias `E2E_WEB_URL`; 3000 for Next, 3001 for Blazor in the fake profile). CI runs both in the `e2e` matrix (`e2e` = Next, `e2e-blazor`), each on its own fresh fake profile.

- **Interactive Blazor pages.** The page is HTML first and interactive only after the websocket answers `StartCircuit`; input before that is discarded without an error. Use `gotoInteractive(page, url)` (in `harness/circuit.ts`) instead of `page.goto` for a page that may be interactive (it waits only when the prerendered HTML carries a server component marker), and `watchCircuit(page)` taken **before** a click that navigates to one (`await circuit.ready()` after). On Next they are no-ops. Never sleep for a circuit.
- **Pages not ported yet.** Start a test with `await requirePorted(page.request, "/route", ...)` (`harness/parity.ts`): on `HOST=blazor` it skips the test with `not ported yet: <route> (owner <branch>; docs/operations/PARITY.md)` when the host answers 404 for a route, so the skips disappear as pages land. `PARITY_STRICT=1` (the cutover gate) turns them into failures. Add the owner prefix to `OWNERS` for a new route family.
- **No BFF on Blazor.** `signInAs`/`signInViaBff` sign the browser context in through the real form and keep the user's token for the context; `bff(ctx, ..., "/api/proxy/<service>/...")` and the booking helpers call the service directly with it on `HOST=blazor`. What a journey is about still goes through the UI.
- **Which specs run where** (`playwright.config.ts`): both hosts run every journey; `*.blazor.spec.ts` is Blazor-only (J7's variant against `/logowanie`), `*.next.spec.ts` and the original `j7-foreign-origin.spec.ts` (it posts through the BFF) are Next-only. Name a Blazor-only spec `jN-....blazor.spec.ts`; there is no spec list to edit.
- **Sign-in on Blazor** is the form post of `signInViaFormPost` (the session cookie `gt_session`); `signInViaBff` also fetches the user's token from the identity service for API set-up (`accessTokenOf`).


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
