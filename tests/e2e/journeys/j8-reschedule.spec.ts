import { expect, test } from "../harness/fixtures";
import { restoreClock } from "../harness/clock";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { BookingDetailPage, ReschedulePage } from "../harness/pages-bookings";
import { addDays, bookAndPay, day, getBooking, openSlots, publicProfile, registerDemoUser, uniqueDemoName } from "../harness/booking-support";

// J8 (T-11.20, CONTRACTS 4.2): a client moves a paid booking to another free time, once. Set-up (a fresh client, a paid booking in the fake profile) goes through the services;
// the move itself, the flash, the new time and the missing entry afterwards are driven through the UI, and the follow-up state (new start, old slot free, payment line) is read
// from Booking's API. The clock is only read here: the booking is made 72 h ahead, so the 24 h rule needs no time travel.
test.describe("J8 reschedule: one move, the old slot is free again", () => {
  const SLUG = "demo-ewa-wzorcowa"; // no other journey books this practitioner

  test.afterAll(async () => {
    await restoreClock(); // a no-op unless a test moved it; every row this journey writes belongs to its own fresh client, so no reseed
  });

  test("the client moves a paid booking once, sees the flash and the new time, and has no second move", async ({ page, context, clock }) => {
    const profile = await publicProfile(SLUG);
    const offering = profile.offerings[0];
    await registerDemoUser(context, uniqueDemoName("j8-move"));
    const placed = await bookAndPay(context, { practitionerId: profile.id, offeringId: offering.id, now: new Date((await clock.read()).now), minAheadHours: 72 });
    const before = await getBooking(context, placed.id);

    const detail = new BookingDetailPage(page);
    await detail.goto(placed.id);
    await expect(detail.value("Potwierdzona")).toBeVisible();
    await expect(detail.value("Opłacona")).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    await detail.rescheduleLink().click();
    const reschedule = new ReschedulePage(page);
    await expect(reschedule.heading()).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`/moje/rezerwacje/${placed.id}/zmien-termin`));
    const firstTime = await reschedule.firstTime();
    await expectNoSeriousA11yViolations(page);

    // the picker's button carries the UTC start of the time it posts (data-utc is the page's own machine-readable twin of the label, read here to know what to expect)
    const newStartUtc = await firstTime.getAttribute("data-utc");
    expect(newStartUtc, "the picked time carries its UTC start").toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/);
    const newStart = new Date(newStartUtc!);
    expect(newStart.getTime(), "another time than the booking holds").not.toBe(placed.start.getTime());
    await firstTime.click();

    // back on the detail, with the flash of the move and the new time
    await expect(page).toHaveURL(new RegExp(`/moje/rezerwacje/${placed.id}\\?.*done=rescheduled`));
    await expect(page.getByText("Termin został zmieniony.")).toBeVisible();
    const hhmm = (d: Date) => new Intl.DateTimeFormat("pl-PL", { timeZone: "Europe/Warsaw", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(d);
    const newEnd = new Date(newStart.getTime() + (placed.end.getTime() - placed.start.getTime()));
    await expect(detail.value(`${hhmm(newStart)}–${hhmm(newEnd)}`)).toBeVisible();
    await expect(detail.value("Potwierdzona")).toBeVisible(); // the status did not change ...
    await expect(detail.value("Opłacona")).toBeVisible(); // ... and neither did the payment line
    await expectNoSeriousA11yViolations(page);

    // the follow-up state: Booking moved the start, left the money alone, and the old slot is offered again while the new one is not
    const after = await getBooking(context, placed.id);
    expect(after.status).toBe("Confirmed");
    expect(new Date(after.startUtc).getTime()).toBe(newStart.getTime());
    expect(after.price.amountMinor).toBe(before.price.amountMinor);
    expect(after.payment.status).toBe("Succeeded");
    expect(after.payment.refunded.amountMinor).toBe(0);
    const free = await openSlots(profile.id, offering.id, day(addDays(placed.start, -1)), day(addDays(placed.start, 1)));
    expect(free.map((s) => s.getTime()), "the old slot is free again").toContain(placed.start.getTime());
    const freeAtNew = await openSlots(profile.id, offering.id, day(addDays(newStart, -1)), day(addDays(newStart, 1)));
    expect(freeAtNew.map((s) => s.getTime()), "the new slot is taken").not.toContain(newStart.getTime());

    // the one allowed move is used: no entry on a fresh read of the detail, and the move page itself says so in words
    await detail.goto(placed.id);
    await expect(detail.cancelButton()).toBeVisible(); // the page has rendered its actions
    await expect(detail.rescheduleLink()).toHaveCount(0);
    await page.goto(`/moje/rezerwacje/${placed.id}/zmien-termin`);
    await expect(reschedule.heading()).toBeVisible();
    await expect(reschedule.refused()).toBeVisible();
    await expect(reschedule.times()).toHaveCount(0);
  });
});
