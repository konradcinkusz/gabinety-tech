import { expect, test } from "../harness/fixtures";
import { restoreClock } from "../harness/clock";
import { resetAndReseed } from "../harness/demo-data";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { BookingDetailPage } from "../harness/pages-bookings";
import { bookAndPay, getBooking, publicProfile, registerDemoUser, uniqueDemoName } from "../harness/booking-support";

// J5 (SPEC 10): a client cancels 24 h or more ahead and the refund shows; cancels late and does not (CONTRACTS 8.1, 9.2).
// Set-up (a fresh client, a paid booking) goes through the BFF; the cancel itself and what the client then sees are driven through the UI,
// and the follow-up state (booking status, payment) is read from Booking's API. Business time moves only with the test clock.
test.describe("J5 cancel: refund early, none late", () => {
  const SLUG = "demo-bartosz-testowy";

  test.describe.configure({ retries: 0 }); // a retry would only repeat the 6 minute clean-up below
  let failed = false;

  // CI diagnostics: the list reporter prints failure details only at the very end of the run, so say why a test failed as it happens (assertion text only).
  test.afterEach(async ({}, testInfo) => {
    if (testInfo.status !== testInfo.expectedStatus) {
      failed = true;
      console.log(`[${testInfo.title}] ${(testInfo.error?.message ?? "failed").split("\n").slice(0, 12).join(" | ")}`);
    }
  });

  // Every row this journey writes is unique to it (a fresh `demo-` client and its bookings). CI runs on a fresh stack, so the 1 to 6 minute reseed
  // only runs on request, for a local re-run: E2E_RESEED=1 (README).
  test.afterAll(async () => {
    test.setTimeout(11 * 60_000);
    await restoreClock();
    if (process.env.E2E_RESEED === "1" && !failed) await resetAndReseed();
  });

  test("cancelling more than 24 h ahead shows the refund and the payment is Refunded", async ({ page, context, clock }) => {
    const profile = await publicProfile(SLUG);
    const offering = profile.offerings[0];
    await registerDemoUser(context, uniqueDemoName("j5-early"));
    const b = await bookAndPay(context, { practitionerId: profile.id, offeringId: offering.id, now: new Date((await clock.read()).now), minAheadHours: 72 });

    const detail = new BookingDetailPage(page);
    await detail.goto(b.id);
    await expect(detail.value("Potwierdzona")).toBeVisible();
    await expect(detail.value("Opłacona")).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await detail.cancelButton().click();
    // UX-031 (audit P-BKS-2, F-11): the dialog states the outcome for THIS booking, not the general rule; the outcome below is what proves it
    await expect(detail.cancelDialog()).toContainText("Otrzymasz zwrot");
    await detail.confirmCancelAndWait();
    await expect(detail.value(/^Anulowana przez klienta$/)).toBeVisible();
    await expect(detail.value("Zwrócona w całości")).toBeVisible();
    const m = offering.price.amountMinor; // below 1000 zł: no thousands separator
    await expect(detail.value(/Zwrócono:/)).toContainText(`${Math.floor(m / 100)},${String(m % 100).padStart(2, "0")}`);
    await expectNoSeriousA11yViolations(page);

    await expect.poll(async () => (await getBooking(context, b.id)).payment.status, { message: "payment of the early cancel" }).toBe("Refunded");
    const view = await getBooking(context, b.id);
    expect(view.status).toBe("CancelledByClient");
    expect(view.payment.refunded.amountMinor).toBe(offering.price.amountMinor); // 100 %, CONTRACTS 9.2
  });

  test("cancelling inside 24 h shows no refund and the payment is unchanged", async ({ page, context, clock }) => {
    const profile = await publicProfile(SLUG);
    const offering = profile.offerings[0];
    await registerDemoUser(context, uniqueDemoName("j5-late"));
    const b = await bookAndPay(context, { practitionerId: profile.id, offeringId: offering.id, now: new Date((await clock.read()).now), minAheadHours: 72, skip: 3 });

    // 12 h before the start: well inside the 24 h window, and the clock (never a sleep) is what gets us there
    const now = new Date((await clock.read()).now);
    await clock.advanceHours((b.start.getTime() - now.getTime()) / 3_600_000 - 12);

    const detail = new BookingDetailPage(page);
    await detail.goto(b.id);
    await expect(detail.value("Opłacona")).toBeVisible();
    await detail.cancelButton().click();
    await expect(detail.cancelDialog()).toContainText("nie otrzymasz zwrotu"); // UX-031: the computed outcome of this booking
    await detail.confirmCancelAndWait();
    await expect(detail.value("Anulowana przez klienta (bez zwrotu)")).toBeVisible();
    await expect(detail.value("Opłacona")).toBeVisible(); // the payment line did not change ...
    await expect(page.getByText(/Zwrócono:/)).toHaveCount(0); // ... and shows no refund
    await expectNoSeriousA11yViolations(page);

    const view = await getBooking(context, b.id);
    expect(view.status).toBe("LateCancelledByClient");
    expect(view.payment.status).toBe("Succeeded");
    expect(view.payment.refunded.amountMinor).toBe(0);
  });
});
