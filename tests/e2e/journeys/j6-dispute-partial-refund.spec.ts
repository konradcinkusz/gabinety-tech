import { expect, test } from "../harness/fixtures";
import { restoreClock } from "../harness/clock";
import { resetAndReseed } from "../harness/demo-data";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { AdminDisputesPage, BookingDetailPage } from "../harness/pages-bookings";
import {
  actorContext, addDays, addOffering, bookAndPay, day, feeOf, getBooking, ledgerDownload, ledgerRows, type LedgerRow, publicProfile, registerDemoUser, uniqueDemoName,
} from "../harness/booking-support";

// J6 (SPEC 10): a dispute is opened, an administrator resolves it with a partial refund, the ledger CSV shows the entries.
// The numbers are CONTRACTS 9.1 (T-08.05): price 25000, refund 10000, kept 15000, fee (15000 * 1500 + 5000) / 10000 = 2250, payout 12750.
// A fresh client and an own 25000 offering make the figures independent of the seeded catalogue and of the "one fee per pair" rule.
const PRICE = 25000, REFUND = 10000, KEPT = PRICE - REFUND, FEE = 2250, PAYOUT = KEPT - FEE;

test.describe("J6 dispute and partial refund", () => {
  test.describe.configure({ retries: 0 });
  let failed = false;

  // CI diagnostics: the list reporter prints failure details only at the very end of the run, so say why a test failed as it happens (assertion text only).
  test.afterEach(async ({}, testInfo) => {
    if (testInfo.status !== testInfo.expectedStatus) {
      failed = true;
      console.log(`[${testInfo.title}] ${(testInfo.error?.message ?? "failed").split("\n").slice(0, 12).join(" | ")}`);
    }
  });

  test.afterAll(async () => {
    test.setTimeout(11 * 60_000);
    await restoreClock();
    // Only on request (E2E_RESEED=1, README): CI runs on a fresh stack and a reseed is 1 to 6 minutes of its budget. Removes the fresh client, the extra offering and the booking.
    if (process.env.E2E_RESEED === "1" && !failed) await resetAndReseed();
  });

  test("the arithmetic of CONTRACTS 9.1 holds for the journey's figures", () => {
    expect(feeOf(KEPT)).toBe(FEE);
    expect(PAYOUT).toBe(12750);
    for (const [price, fee] of [[25000, 3750], [33333, 5000], [5000, 750], [1, 0], [150000, 22500]]) expect(feeOf(price)).toBe(fee); // CONTRACTS 9.1 vectors
  });

  test("dispute, partial refund by the administrator, ledger CSV", async ({ page, context, browser, clock }) => {
    test.setTimeout(4 * 60_000); // includes the payout job, which runs every 60 s

    // set-up: a practitioner's own 25000 offering, a fresh client, a paid booking
    const profile = await publicProfile("demo-dariusz-fikcyjny");
    const practitioner = await actorContext(browser, "demo-practitioner-4");
    const offeringId = await addOffering(practitioner.request, profile.offerings[0], PRICE, uniqueDemoName("j6").slice(5));
    await practitioner.close();
    await registerDemoUser(context, uniqueDemoName("j6-client"));
    const now = new Date((await clock.read()).now);
    const b = await bookAndPay(context, { practitionerId: profile.id, offeringId, now, minAheadHours: 72 });
    expect((await getBooking(context, b.id)).price.amountMinor).toBe(PRICE);

    // a dispute needs the consultation to have ended (CONTRACTS 4.2): move the clock 30 minutes past the end, well inside the 48 h window
    await clock.advanceMinutes(Math.ceil((b.end.getTime() - now.getTime()) / 60_000) + 30);

    // the client opens the dispute in the UI
    const detail = new BookingDetailPage(page);
    await detail.goto(b.id);
    await expect(detail.value("Potwierdzona")).toBeVisible();
    await detail.openDispute("Konsultacja różniła się od opisu, dane syntetyczne testu.");
    await expect(page.getByText("Spór został zgłoszony.")).toBeVisible();
    await expect(detail.value("Spór w toku")).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    expect((await getBooking(context, b.id)).status).toBe("Disputed");

    // the administrator resolves it with a partial refund of 100,00 zł in the UI
    const admin = await actorContext(browser, "demo-admin");
    try {
      const adminPage = await admin.newPage();
      const disputes = new AdminDisputesPage(adminPage);
      await disputes.goto();
      await expect(disputes.item(b.id)).toBeVisible();
      await expectNoSeriousA11yViolations(adminPage);
      await disputes.resolvePartially(b.id, "100,00", "Częściowy zwrot po sprawdzeniu zgłoszenia, test.");
      // the confirmation states the money in words before anything is saved (CONTRACTS 9.2). It names the refund; the kept amount only when the page
      // could read the price (it falls back to words without it), so the figures themselves are asserted below from Booking's state and the ledger.
      await expect(disputes.item(b.id)).toContainText("100,00");
      await disputes.confirmAndWait(b.id); // a resolved dispute leaves the open list

      // follow-up state: what Booking says
      const view = await getBooking(admin, b.id);
      expect(view.status).toBe("Resolved");
      expect(view.payment.status).toBe("PartiallyRefunded");
      expect(view.payment.refunded.amountMinor).toBe(REFUND);
      expect(view.fee?.amountMinor).toBe(FEE);
      expect(view.payout?.amountMinor).toBe(PAYOUT);

      // the ledger CSV (T-04.08b): our booking's rows only, in a period that covers the moved clock; the payout row comes from the payout job
      const from = day(addDays(now, -1));
      const to = day(addDays(new Date((await clock.read()).now), 1));
      let rows: LedgerRow[] = [];
      let last = "no answer yet";
      await expect
        .poll(
          async () => {
            const r = await ledgerRows(admin, from, to, [b.id]);
            if ("rows" in r) { rows = r.rows; last = "200"; } else last = `${r.status} ${r.code}`;
            return rows.some((x) => x.kind === "PractitionerPayout");
          },
          { timeout: 150_000, intervals: [5_000], message: "payout job writes PractitionerPayout" },
        )
        .toBe(true)
        .catch((e) => { throw new Error(`${e.message} (last ledger answer: ${last}; rows: ${rows.map((x) => x.kind).join(",")})`); });
      const byKind = Object.fromEntries(rows.map((r) => [r.kind, r.amountMinor]));
      expect(byKind).toEqual({ ClientCharge: PRICE, Refund: REFUND, PlatformFee: FEE, PractitionerPayout: PAYOUT });
      expect(rows).toHaveLength(4); // one row each, no PayoutReversal
      expect(byKind.ClientCharge - byKind.Refund).toBe(byKind.PlatformFee + byKind.PractitionerPayout); // kept = fee + payout

      // the download route of the page (streams the same CSV to the browser): 200, text/csv, an attachment with the period in its name, the contract's header
      const dl = await ledgerDownload(admin, from, to);
      expect(dl.status, "ledger download route").toBe(200);
      expect(dl.contentType).toContain("text/csv");
      expect(dl.disposition).toContain(`attachment; filename="ledger-${from}-${to}.csv"`);
      expect(dl.header).toBe("booking_id,kind,amount_minor,currency,created_at");

      await adminPage.goto("/admin/ksiega");
      await expectNoSeriousA11yViolations(adminPage);
    } finally {
      await admin.close();
    }
  });
});
