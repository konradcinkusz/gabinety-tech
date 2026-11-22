import { test, expect } from "../harness/fixtures";
import { signInViaBff } from "../harness/auth";
import { restoreClock } from "../harness/clock";
import { urls } from "../harness/env";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import {
  reseedIfRequested, reviewAttemptStatus, bookingIdFromUrl, createBooking, createReview, earliestOnDistinctDays, freeSlots, getBooking, publicProfile, simulatePayment, waitForStatus, warsawDate,
} from "../harness/booking-api";
import { BookingFormPage, MockCheckoutPage, PractitionerProfilePage, ReviewFormPage } from "../harness/pages-booking";

/**
 * J3 (SPEC 10): a client books and pays on the mock, the practitioner sees the booking, time advances (CONTRACTS 10.4), the booking completes,
 * the client reviews, the review appears and the rating shows "Nowy" until three.
 *
 * Practitioner: demo-celina-przykladowska (demo-practitioner-3, the seeded profile with no real review: SPEC 7 "new"). Three bookings are made
 * (one through the UI, two through the same BFF calls) so that the rating can be asserted at 2 reviews ("Nowy") and at 3 (the average).
 * Everything is `demo-` data; the seeded bookings and reviews of other practitioners are not touched. Unique data per run: the free slots are
 * read fresh, so no fixed date is assumed, and time is moved relative to the clock's own `now`.
 */
const SLUG = "demo-celina-przykladowska";
const NAME = "Celina Przykładowska";
const PRACTITIONER_USER = "demo-practitioner-3";
const BRIEFS = [
  "Potrzebuję pomocy z architekturą prostego prototypu z modelem językowym (j3-1).",
  "Chcę ocenić koszty i ryzyka integracji modelu językowego z aplikacją (j3-2).",
  "Proszę o przegląd sposobu oceny jakości odpowiedzi modelu językowego (j3-3).",
];
const REVIEWS = [
  { rating: 5, text: "Konsultacja przebiegła rzetelnie, dostałem konkretny plan prototypu (j3-1)." },
  { rating: 4, text: "Dobra rozmowa o kosztach i ryzykach, kilka praktycznych wskazówek (j3-2)." },
  { rating: 3, text: "Pomocne uwagi o ocenie jakości, trochę zabrakło czasu na pytania (j3-3)." },
];
const AVERAGE = (5 + 4 + 3) / 3;

test.describe("J3 book, pay, complete, review", () => {
  // No retry: a retry meets the data the failed attempt left (J3 needs a profile with no review).
  test.describe.configure({ mode: "serial", retries: 0 });
  // The clock is global to the fake profile: whatever happens, leave it at the baseline, then remove the demo rows this journey wrote (SPEC 7).
  test.afterAll(async () => {
    test.setTimeout(900_000); // E2E_RESEED=1 only: the reseed takes 1 to 6 minutes, the default hook timeout is 60 s
    await restoreClock();
    await reseedIfRequested();
  });

  test("a client books and pays, the booking completes after 48 h, three reviews turn \"Nowy\" into an average", async ({ page, context, browser, signInAs, clock }) => {
    test.setTimeout(420_000); // two turns of Booking's 60 s auto-complete job plus the UI steps
    await signInAs("demo-client");
    const client = context.request;

    // ---- the starting point: a profile with no review shows "Nowy" ----
    const before = await publicProfile(SLUG);
    expect(before.isNew, "start from the seeded profile with no reviews (reset the demo data first)").toBe(true);
    expect(before.rating ?? null).toBeNull();
    const offering = before.offerings[0]!;
    const price = offering.price.amountMinor;

    const profile = new PractitionerProfilePage(page);
    await profile.goto(SLUG, NAME);
    await expect(profile.newBadge()).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    // ---- 1. book and pay through the UI ----
    const slot = await profile.firstSlot(offering.title); // the picker found free slots
    const link = new URL((await slot.getAttribute("href"))!, urls.web);
    const startUtc = link.searchParams.get("start")!;
    expect(link.searchParams.get("gabinet")).toBe(SLUG); // the slot link carries the profile slug (#99)
    await profile.openBookingForm(slot);
    const form = new BookingFormPage(page);
    await expect(page.getByRole("heading", { level: 1, name: "Rezerwacja konsultacji" })).toBeVisible();
    await expect(page.getByText(offering.title)).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    const bookingIds: string[] = [await form.submit(BRIEFS[0]!)];

    let view = await getBooking(client, bookingIds[0]!);
    expect(view.status).toBe("PendingPayment");
    expect(view.payment.status).toBe("Created");
    await expect(page.getByRole("heading", { level: 1, name: "Płatność (tryb demonstracyjny)" })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await new MockCheckoutPage(page).paySuccessfully();
    expect(bookingIdFromUrl(page.url())).toBe(bookingIds[0]);
    await expectNoSeriousA11yViolations(page);

    view = await getBooking(client, bookingIds[0]!);
    expect(view.status).toBe("Confirmed");
    expect(view.payment.status).toBe("Succeeded");
    expect(view.price.amountMinor).toBe(price);
    expect(new Date(view.startUtc).toISOString()).toBe(new Date(startUtc).toISOString());

    // ---- 2. the practitioner sees the booking (their own session, a second browser context) ----
    const pctx = await browser.newContext({ baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
    try {
      await signInViaBff(pctx, PRACTITIONER_USER);
      const pp = await pctx.newPage();
      // the API first: the practitioner's own view of the booking (status, payment, the brief that only a Confirmed booking shows them)
      const seen = await getBooking(pctx.request, bookingIds[0]!);
      expect(seen.status).toBe("Confirmed");
      expect(seen.payment.status).toBe("Succeeded");
      expect(seen.brief?.text).toBe(BRIEFS[0]);
      await pp.goto("/moje/rezerwacje?role=practitioner");
      const row = pp.getByRole("listitem").filter({ hasText: "Potwierdzona" }).filter({ hasText: offering.title });
      await expect(row).toHaveCount(1);
      await expectNoSeriousA11yViolations(pp);
      await row.getByRole("link", { name: "Szczegóły" }).click();
      await pp.waitForURL(new RegExp(bookingIds[0]!));
      // the detail page reads the booking in the browser: reload while it shows the load error instead of the booking
      await expect(async () => {
        if (!(await pp.getByText("Opis problemu (klient)").isVisible())) await pp.reload();
        await expect(pp.getByText("Opis problemu (klient)")).toBeVisible({ timeout: 5_000 });
      }).toPass({ timeout: 60_000, intervals: [2_000, 5_000] });
      await expect(pp.getByText(BRIEFS[0]!)).toBeVisible();
      await expect(pp.getByText("Opłacona")).toBeVisible();
    } finally {
      await pctx.close();
    }

    // ---- 3. two more consultations, booked and paid through the same calls the UI makes ----
    const others = earliestOnDistinctDays(await freeSlots(client, before.id, offering.id), 2, [warsawDate(startUtc)]);
    for (const [i, s] of others.entries()) {
      const { bookingId } = await createBooking(client, offering.id, s.startUtc, BRIEFS[i + 1]!);
      await simulatePayment(client, bookingId, "succeeded");
      bookingIds.push(bookingId);
    }
    const confirmed = await Promise.all(bookingIds.map((id) => getBooking(client, id)));
    for (const b of confirmed) {
      expect(b.status).toBe("Confirmed");
      expect(b.payment.status).toBe("Succeeded");
    }

    // ---- 4. business time passes: past the last end and 48 h (Booking:CompleteAfterHours); then the job completes the bookings ----
    const lastEnd = Math.max(...confirmed.map((b) => Date.parse(b.endUtc)));
    const now = Date.parse((await clock.read()).now);
    await clock.advanceMinutes(Math.ceil((lastEnd + 49 * 3_600_000 - now) / 60_000));
    for (const id of bookingIds) await waitForStatus(client, id, "Completed");
    for (const id of bookingIds) {
      const done = await getBooking(client, id);
      expect(done.status).toBe("Completed");
      expect(done.payment.status).toBe("Succeeded"); // completing does not touch the payment
      expect(done.canReview).toBe(true);
    }

    // ---- 5. the first review through the UI: still "Nowy" ----
    const reviewForm = new ReviewFormPage(page);
    await reviewForm.goto(bookingIds[0]!);
    await expectNoSeriousA11yViolations(page);
    await reviewForm.submit(REVIEWS[0]!.rating, REVIEWS[0]!.text);
    await expect(page.getByRole("heading", { name: "Dziękujemy za opinię" })).toBeVisible();
    await expect(page.getByText("Opinia została opublikowana na profilu programisty.")).toBeVisible();
    expect(await reviewAttemptStatus(client, bookingIds[0]!, 1, REVIEWS[0]!.text)).toBe(409); // one review per booking (the flag canReview does not know)

    // ---- 6. reviews 2 (still "Nowy") and 3 (the average) ----
    expect((await createReview(client, bookingIds[1]!, REVIEWS[1]!.rating, REVIEWS[1]!.text)).status).toBe("Published");
    const atTwo = await publicProfile(SLUG);
    expect(atTwo.isNew, "two reviews are not enough for a rating").toBe(true);
    expect(atTwo.rating ?? null).toBeNull();

    expect((await createReview(client, bookingIds[2]!, REVIEWS[2]!.rating, REVIEWS[2]!.text)).status).toBe("Published");
    // Reputation pushes the summary to the Directory after the review: wait for it to show.
    await expect
      .poll(async () => (await publicProfile(SLUG)).rating?.count ?? 0, { message: "the third review reaches the Directory", timeout: 60_000, intervals: [1_000, 2_000, 5_000] })
      .toBe(3);
    const atThree = await publicProfile(SLUG);
    expect(atThree.isNew).toBe(false);
    expect(atThree.rating!.average).toBeCloseTo(AVERAGE, 1);

    // ---- 7. the reviews are visible on the public page (the list is read live; the rating block of the page is cached 5 min by the web app) ----
    await page.goto(`/gabinet/${SLUG}`);
    const reviews = profile.reviewsSection();
    for (const r of REVIEWS) {
      await expect(reviews.getByText(r.text)).toBeVisible();
      await expect(reviews.getByText(`Ocena: ${r.rating} z 5`)).toBeVisible();
    }
    await expectNoSeriousA11yViolations(page);
  });
});
