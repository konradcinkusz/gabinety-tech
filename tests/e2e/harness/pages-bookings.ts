import { expect, type Locator, type Page } from "@playwright/test";

/** Page objects of the booking detail (/moje/rezerwacje/{id}) and the administrator's disputes list (/admin/spory), for J5 and J6. Role and label locators only. */
export class BookingDetailPage {
  constructor(readonly page: Page) {}
  async goto(bookingId: string) {
    await this.page.goto(`/moje/rezerwacje/${bookingId}?role=client`);
    await expect(this.page.getByRole("heading", { level: 2 }).first()).toBeVisible();
  }
  /** The value cells (`dd`) carry the status badge and the payment line; they have the role `definition`. */
  value(text: string | RegExp): Locator { return this.page.getByRole("definition").filter({ hasText: text }); }
  cancelButton(): Locator { return this.page.getByRole("button", { name: "Anuluj rezerwację" }); }
  cancelDialog(): Locator { return this.page.getByRole("alertdialog", { name: "Czy na pewno anulować rezerwację?" }); }
  confirmCancel(): Locator { return this.page.getByRole("button", { name: "Tak, anuluj rezerwację" }); }
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
    await this.page.goto("/admin/spory");
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
}
