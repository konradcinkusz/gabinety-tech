import { expect, type Locator, type Page } from "@playwright/test";
import { gotoInteractive } from "./circuit";

/** Page objects of the booking detail (/moje/rezerwacje/{id}) and the administrator's disputes list (/admin/spory), for J5 and J6. Role and label locators only. */
export class BookingDetailPage {
  constructor(readonly page: Page) {}
  /** Opens the detail; a page that shows an error banner instead (the services answered 429/503 under the shared rate limit) is reloaded a few times. */
  async goto(bookingId: string) {
    const heading = this.page.getByRole("heading", { level: 2 }).first();
    for (let attempt = 1; ; attempt++) {
      await gotoInteractive(this.page, `/moje/rezerwacje/${bookingId}?role=client`);
      try {
        await expect(heading).toBeVisible({ timeout: 8_000 });
        return;
      } catch (e) {
        if (attempt >= 4) throw e;
        await this.page.waitForTimeout(5_000); // waiting out a rate limit window, not business time
      }
    }
  }
  /** The value cells (`dd`) carry the status badge and the payment line; they have the role `definition`. */
  value(text: string | RegExp): Locator { return this.page.getByRole("definition").filter({ hasText: text }); }
  cancelButton(): Locator { return this.page.getByRole("button", { name: "Anuluj rezerwację" }); }
  cancelDialog(): Locator { return this.page.getByRole("alertdialog", { name: "Czy na pewno anulować rezerwację?" }); }
  confirmCancel(): Locator { return this.page.getByRole("button", { name: "Tak, anuluj rezerwację" }); }
  /**
   * Confirms the cancel and waits for the success line. A danger banner (the services answered 429/503/...) is logged (product copy only) and the confirm
   * is pressed again a few times: the cancel is all-or-nothing on the server, so a repeat after a failure cannot cancel twice.
   */
  async confirmCancelAndWait() {
    const done = this.page.getByText("Rezerwacja została anulowana.");
    for (let attempt = 1; ; attempt++) {
      await this.confirmCancel().click();
      try {
        await expect(done).toBeVisible({ timeout: 10_000 });
        return;
      } catch (e) {
        const banners = await this.page.getByRole("alert").allInnerTexts();
        console.log(`[cancel attempt ${attempt}] banner: ${banners.join(" | ") || "none"}`);
        if (attempt >= 4) throw e;
        await this.page.waitForTimeout(5_000); // waiting out a rate limit window, not business time
      }
    }
  }
  /** The reschedule entry (T-11.11): a link on the detail while the service allows a move; absent once the one allowed move is used. */
  rescheduleLink(): Locator { return this.page.getByRole("link", { name: "Zmień termin" }); }
  disputeButton(): Locator { return this.page.getByRole("button", { name: "Zgłoś spór" }); }
  async openDispute(text: string) {
    await this.disputeButton().click();
    await this.page.getByLabel("Opis sporu").fill(text);
    await this.page.getByRole("button", { name: "Wyślij zgłoszenie" }).click();
  }
}

export class AdminDisputesPage {
  constructor(readonly page: Page) {}
  async goto() {
    await gotoInteractive(this.page, "/admin/spory");
    await expect(this.page.getByRole("heading", { level: 1, name: "Spory" })).toBeVisible();
  }
  /** One dispute card: the list item that names the booking id. */
  item(bookingId: string): Locator { return this.page.getByRole("listitem").filter({ hasText: bookingId }); }
  async resolvePartially(bookingId: string, amountPln: string, note: string) {
    const item = this.item(bookingId);
    await item.getByRole("button", { name: "Rozstrzygnij" }).click();
    await item.getByRole("radio", { name: "Zwróć klientowi część" }).check();
    await item.getByLabel("Kwota zwrotu w zł").fill(amountPln);
    await item.getByLabel("Notatka").fill(note);
    await item.getByRole("button", { name: "Przejdź do potwierdzenia" }).click();
  }
  confirmButton(bookingId: string): Locator { return this.item(bookingId).getByRole("button", { name: "Potwierdź rozstrzygnięcie" }); }
  /** Confirms and waits until the dispute leaves the open list; a danger banner is logged (product copy only) and the confirm pressed again a few times. */
  async confirmAndWait(bookingId: string) {
    for (let attempt = 1; ; attempt++) {
      const button = this.confirmButton(bookingId);
      if (await button.isVisible()) await button.click();
      try {
        await expect(this.item(bookingId)).toHaveCount(0, { timeout: 10_000 });
        return;
      } catch (e) {
        const banners = await this.item(bookingId).getByRole("alert").allInnerTexts();
        console.log(`[resolve attempt ${attempt}] banner: ${banners.join(" | ") || "none"}`);
        if (attempt >= 4) throw e;
        await this.page.waitForTimeout(5_000);
      }
    }
  }
}

/** /moje/rezerwacje/{id}/zmien-termin: the booking's summary and the slot picker of its offer, each free time a submit button. */
export class ReschedulePage {
  constructor(readonly page: Page) {}
  heading(): Locator { return this.page.getByRole("heading", { level: 1, name: "Zmień termin" }); }
  /** The free times shown (buttons named like `14:30`). */
  times(): Locator { return this.page.getByRole("button", { name: /^\d{2}:\d{2}$/ }); }
  /** The refusal callout of a booking that cannot be moved (again). */
  refused(): Locator { return this.page.getByText("Tej rezerwacji nie można już przełożyć", { exact: false }); }
  /** The picker reads the slots from Booking, which may answer 429 for a moment under the shared rate limit: reload until a time is shown (bounded). */
  async firstTime(): Promise<Locator> {
    const first = this.times().first();
    await expect(async () => {
      if (!(await first.isVisible())) await this.page.reload();
      await expect(first).toBeVisible({ timeout: 8_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
    return first;
  }
}
