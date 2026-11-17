import { APPROVED_DEMO_PRACTITIONERS } from "./env";
import { expect, test } from "./fixtures";
import { HomePage, PractitionerListPage } from "./pages";
import { expectNoSeriousA11yViolations } from "./a11y";

// Harness smoke (T-08.03a): proves the config, the fixtures and the profile work end to end. Not a journey (those are journeys/jN-*.spec.ts).
test.describe("harness smoke", () => {
  test("home page renders and has no serious a11y violations", async ({ page }) => {
    const home = new HomePage(page);
    await home.goto();
    await expect(home.heading()).toHaveCount(1);
    await expectNoSeriousA11yViolations(page);
  });

  test("/programisci lists the 6 seeded practitioners", async ({ page }) => {
    const list = new PractitionerListPage(page);
    await list.goto();
    for (const p of APPROVED_DEMO_PRACTITIONERS) await expect(list.profileLink(p.name)).toBeVisible();
    await expect(list.results()).toHaveCount(APPROVED_DEMO_PRACTITIONERS.length);
  });

  test("the test clock moves and is restored", async ({ clock }) => {
    const before = await clock.read(); // relative: whatever offset the clock has now, not an assumed 0
    await clock.advanceHours(49);
    const moved = await clock.read();
    expect(moved.offsetMinutes - before.offsetMinutes).toBe(49 * 60);
    await clock.advanceHours(-49); // back to exactly where it was
    expect((await clock.read()).offsetMinutes).toBe(before.offsetMinutes);
  });

  test("the previous test left the clock restored", async ({ clock }) => {
    expect((await clock.read()).offsetMinutes).toBe(0); // the fixture restores 0; globalSetup waited for the seeder, so nothing else moves it
  });

  test("a demo user signs in", async ({ page, context, signInAs }) => {
    await signInAs("demo-client");
    await page.goto("/moje/rezerwacje");
    await expect(page).not.toHaveURL(/\/logowanie/);
    expect((await context.cookies()).length).toBeGreaterThan(0);
  });
});
