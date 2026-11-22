import { test, expect } from "../harness/fixtures";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { reseedIfRequested, freeSlots, getBooking, publicProfile } from "../harness/booking-api";
import { BookingFormPage, MockCheckoutPage, PractitionerProfilePage } from "../harness/pages-booking";
import { urls } from "../harness/env";

/**
 * J4 (SPEC 10): the Assistant is down. The fake model answers `[[unavailable]]` in the text with "unavailable" (FakeLlmGateway), so asking for help with
 * such a brief gets the plain-form note and no suggestion, and the brief form still books. Practitioner: demo-dariusz-fikcyjny (not used by J3).
 */
const SLUG = "demo-dariusz-fikcyjny";
const NAME = "Dariusz Fikcyjny";
const BRIEF = "Chcę zautomatyzować obieg faktur między skrzynką a arkuszem [[unavailable]] (j4).";

test.describe("J4 the Assistant is down", () => {
  test.describe.configure({ retries: 0 }); // see J3: a retry meets the data the failed attempt left
  test.afterAll(async () => {
    test.setTimeout(900_000); // E2E_RESEED=1 only: the reseed takes 1 to 6 minutes, the default hook timeout is 60 s
    await reseedIfRequested(); // SPEC 7 cleanup, only with E2E_RESEED=1 (CI runs on a fresh stack)
  });

  test("the brief form still books and no suggestion is shown", async ({ page, context, signInAs }) => {
    await signInAs("demo-client");
    const target = await publicProfile(SLUG);
    const offering = target.offerings[0]!;
    const profile = new PractitionerProfilePage(page);
    await profile.goto(SLUG, NAME);
    const slot = await profile.firstSlot(offering.title);
    const link = new URL((await slot.getAttribute("href"))!, urls.web);
    const startUtc = link.searchParams.get("start")!;
    expect(link.searchParams.get("gabinet")).toBe(SLUG); // the slot link carries the profile slug (#99)
    await profile.openBookingForm(slot);

    const form = new BookingFormPage(page);
    await form.brief().fill(BRIEF);
    await form.askAssistant().click();
    // the plain-form note, and nothing of a suggestion
    await expect(form.assistantUnavailableNote()).toBeVisible();
    for (const part of form.suggestionParts()) await expect(part).toHaveCount(0);
    await expect(form.brief()).toHaveValue(BRIEF); // the brief was not touched
    await expectNoSeriousA11yViolations(page);

    // the form still books
    await form.pay().click();
    await page.waitForURL(/\/checkout\/mock\//);
    const bookingId = /\/checkout\/mock\/([^/?#]+)/.exec(page.url())![1]!;
    const pending = await getBooking(context.request, bookingId);
    expect(pending.status).toBe("PendingPayment");
    await new MockCheckoutPage(page).paySuccessfully();

    const booking = await getBooking(context.request, bookingId);
    expect(booking.status).toBe("Confirmed");
    expect(booking.payment.status).toBe("Succeeded");
    expect(new Date(booking.startUtc).toISOString()).toBe(new Date(startUtc).toISOString());
    expect(booking.brief?.aiAssisted, "no suggestion was applied").toBe(false);
    expect(booking.brief?.problemSlug ?? null).toBeNull();
    expect(booking.brief?.text).toBe(BRIEF);
    expect((await freeSlots(context.request, target.id, offering.id)).map((s) => Date.parse(s.startUtc))).not.toContain(Date.parse(startUtc)); // the slot is taken
  });
});
