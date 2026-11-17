import { test, expect } from "../harness/fixtures";
import { resetAndReseed } from "../harness/demo-data";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { freeSlots, getBooking, publicProfile } from "../harness/booking-api";
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
  test.afterAll(async () => {
    await resetAndReseed(); // SPEC 7: remove the demo rows this journey wrote (once per journey)
  });

  test("the brief form still books and no suggestion is shown", async ({ page, context, signInAs }) => {
    await signInAs("demo-client");
    const target = await publicProfile(SLUG);
    const offering = target.offerings[0]!;
    const profile = new PractitionerProfilePage(page);
    await profile.goto(SLUG, NAME);
    const slot = profile.slotLinks(offering.title).first();
    await expect(slot).toBeVisible();
    const link = new URL((await slot.getAttribute("href"))!, urls.web);
    const startUtc = link.searchParams.get("start")!;
    if (!link.searchParams.has("gabinet")) link.searchParams.set("gabinet", SLUG); // see the J3 note
    await page.goto(link.pathname + link.search);

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
