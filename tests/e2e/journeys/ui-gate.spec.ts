import type { Page } from "@playwright/test";
import { expect, test } from "../harness/fixtures";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { bookAndPay, publicProfile, registerDemoUser, uniqueDemoName } from "../harness/booking-support";

// UI gate (DESIGN-CONTRACT 10 item 4, docs/ui/UX-PLAN.md UX-003): every route the audit reads, on a phone, in both colour schemes: no horizontal page scroll and no serious or
// critical axe violation. The route lists only read (they click nothing and post nothing); the one test that needs a reschedulable booking writes rows of its own fresh `demo-`
// client only, so the gate needs no reseed and cannot disturb the money journeys. The full audit with screenshots stays
// `tests/e2e/tools/ui-audit/audit.mjs` (it writes, so it is run by hand on a throw-away stack). Tap targets are not asserted yet: the page tasks of the plan bring them to zero first.
const PHONE = { width: 390, height: 844 };
const PROFILE = "/gabinet/demo-celina-przykladowska";

const ROUTES: Record<string, string[]> = {
  visitor: ["/", "/programisci", "/programisci/audyt-kodu", PROFILE, "/jak-to-dziala", "/zasady-rankingu", "/cennik", "/regulamin", "/polityka-prywatnosci", "/pomoc", "/logowanie", "/zapomniane-haslo", "/rejestracja", "/rejestracja/sprawdz-skrzynke"],
  "demo-client": ["/konto", "/moje/rezerwacje"],
  "demo-practitioner-3": ["/gabinet-programisty", "/gabinet-programisty/profil", "/gabinet-programisty/oferty", "/gabinet-programisty/dostepnosc", "/gabinet-programisty/platnosci", "/gabinet-programisty/kalendarz", "/moje/rezerwacje?role=practitioner"],
  "demo-admin": ["/admin/weryfikacja", "/admin/spory", "/admin/ksiega", "/admin/opinie", "/admin/podejrzane"],
};

/** One route on the phone, in both colour schemes: no horizontal page scroll, no serious or critical axe violation. */
async function expectPhoneClean(page: Page, who: string, route: string) {
  await page.goto(route, { waitUntil: "domcontentloaded" });
  await page.waitForLoadState("networkidle", { timeout: 8_000 }).catch(() => {});
  for (const scheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme: scheme, reducedMotion: "reduce" }); // reduced motion switches the 120 ms colour transitions off, so axe never reads a colour mid-fade
    await page.waitForTimeout(150);
    const overflow = await page.evaluate(() => ({ scroll: document.documentElement.scrollWidth, client: document.documentElement.clientWidth }));
    expect(overflow.scroll, `${who} ${route} (${scheme}): page is ${overflow.scroll}px wide in a ${overflow.client}px viewport`).toBeLessThanOrEqual(overflow.client + 1);
    await expectNoSeriousA11yViolations(page);
  }
}

test.describe("UI gate: phone width, both schemes", () => {
  for (const [who, routes] of Object.entries(ROUTES)) {
    test(`${who}: no horizontal scroll, no serious axe violation`, async ({ page, signInAs }) => {
      test.setTimeout(120_000);
      if (who !== "visitor") await signInAs(who);
      await page.setViewportSize(PHONE);
      for (const route of routes) await expectPhoneClean(page, who, route);
    });
  }

  // The reschedule page needs a booking the service lets the client move: a fresh client's own paid booking, 72 h ahead (its rows belong to this test alone).
  test("a client with a reschedulable booking: the move page", async ({ page, context, clock }) => {
    test.setTimeout(120_000);
    const profile = await publicProfile("demo-filip-syntetyczny");
    await registerDemoUser(context, uniqueDemoName("gate-move"));
    const b = await bookAndPay(context, { practitionerId: profile.id, offeringId: profile.offerings[0].id, now: new Date((await clock.read()).now), minAheadHours: 72 });
    await page.setViewportSize(PHONE);
    await expectPhoneClean(page, "client", `/moje/rezerwacje/${b.id}/zmien-termin`);
    await expect(page.getByRole("heading", { level: 1, name: "Zmień termin" })).toBeVisible(); // the page itself, not an error shell, is what the checks above ran on
  });
});
