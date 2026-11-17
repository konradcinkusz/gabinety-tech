import { test as base, expect } from "@playwright/test";
import { restoreClock, advanceClock, advanceHours, advanceDays, readClock } from "./clock";
import { resetAndReseed } from "./demo-data";
import { signInViaBff, signInThroughForm } from "./auth";

/**
 * Import `test` and `expect` from here, not from `@playwright/test`.
 *  - `clock`: moves the services' test clock; restored after every test, pass or fail (a journey never leaves time moved).
 *  - `signInAs(userName)`: signs the test's context in as a seeded demo user (fast BFF path); `signInAs.viaForm(page, userName)` uses the form.
 *  - `demoData.resetAndReseed()`: for `afterAll` of a journey that wrote demo data (SPEC 7). Slow (1-6 min): once per journey.
 */
export const test = base.extend<{
  clock: { advanceMinutes: typeof advanceClock; advanceHours: typeof advanceHours; advanceDays: typeof advanceDays; read: typeof readClock };
  signInAs: ((userName: string) => Promise<void>) & { viaForm: typeof signInThroughForm };
  demoData: { resetAndReseed: typeof resetAndReseed };
}>({
  clock: async ({}, use) => {
    await use({ advanceMinutes: advanceClock, advanceHours, advanceDays, read: readClock });
    await restoreClock();
  },
  signInAs: async ({ context }, use) => {
    await use(Object.assign((userName: string) => signInViaBff(context, userName), { viaForm: signInThroughForm }));
  },
  demoData: async ({}, use) => {
    await use({ resetAndReseed });
  },
});

export { expect };
