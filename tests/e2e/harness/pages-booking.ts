import { expect, type Locator, type Page } from "@playwright/test";
import { bookingIdFromUrl } from "./booking-api";

/** Page objects of the booking and review flow (J3, J4). Role and label locators; texts are the Polish strings of web/src/components/{booking,bookings,reviews,profile}/strings.ts. */
export class PractitionerProfilePage {
  constructor(readonly page: Page) {}
  async goto(slug: string, displayName: string) {
    await this.page.goto(`/gabinet/${slug}`);
    await expect(this.page.getByRole("heading", { level: 1, name: displayName })).toBeVisible();
  }
  /** The offering's card: the one list item that holds the offering's heading (the time list inside it has no heading). */
  offering(title: string): Locator {
    return this.page.getByRole("listitem").filter({ has: this.page.getByRole("heading", { level: 3, name: title }) });
  }
  slotLinks(offeringTitle: string): Locator { return this.offering(offeringTitle).getByRole("link", { name: /^\d{2}:\d{2}$/ }); }
  newBadge(): Locator { return this.page.getByText("Nowy", { exact: true }); }
  reviewsSection(): Locator { return this.page.getByRole("region", { name: "Opinie" }); }
}

export class BookingFormPage {
  constructor(readonly page: Page) {}
  brief(): Locator { return this.page.getByLabel("Opisz problem"); }
  askAssistant(): Locator { return this.page.getByRole("button", { name: "Pomóż mi opisać problem" }); }
  pay(): Locator { return this.page.getByRole("button", { name: "Zapłać" }); }
  assistantUnavailableNote(): Locator { return this.page.getByText("Asystent jest chwilowo niedostępny. Opisz problem samodzielnie i zarezerwuj termin."); }
  /** Everything the assistant shows when it does have a suggestion. */
  suggestionParts(): Locator[] {
    return [
      this.page.getByLabel("Propozycja opisu"),
      this.page.getByText("Sugerowana kategoria"),
      this.page.getByRole("button", { name: "Dodaj propozycję do opisu" }),
    ];
  }
  /** Fills the brief, pays and lands on the mock checkout; returns the booking id from its address. */
  async submit(text: string): Promise<string> {
    await this.brief().fill(text);
    await this.pay().click();
    await this.page.waitForURL(/\/checkout\/mock\//);
    return bookingIdFromUrl(this.page.url());
  }
}

export class MockCheckoutPage {
  constructor(readonly page: Page) {}
  succeed(): Locator { return this.page.getByRole("button", { name: "Symuluj udaną płatność" }); }
  /**
   * The checkout and confirmation pages read the booking through the BFF; when the per-address rate limit (60/min, shared with the whole run) answers 429
   * they show "Odśwież stronę" and no button. A reload is what that message asks for, so reload until the page has the booking.
   */
  async paySuccessfully() {
    await expect(async () => {
      if (!(await this.succeed().isVisible())) await this.page.reload();
      await expect(this.succeed()).toBeEnabled({ timeout: 5_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
    await this.succeed().click();
    await this.page.waitForURL(/\/rezerwacja\/potwierdzenie\//);
    await expect(async () => {
      if (!(await this.page.getByText("Rezerwacja potwierdzona. Dziękujemy!").isVisible())) await this.page.reload();
      await expect(this.page.getByText("Rezerwacja potwierdzona. Dziękujemy!")).toBeVisible({ timeout: 5_000 });
    }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
  }
}

export class ReviewFormPage {
  constructor(readonly page: Page) {}
  async goto(bookingId: string) {
    await this.page.goto(`/moje/opinie/${bookingId}`);
    await expect(this.page.getByRole("button", { name: "Wyślij opinię" })).toBeVisible();
  }
  async submit(rating: number, text: string) {
    await this.page.getByRole("radio", { name: `${rating} z 5` }).check();
    await this.page.getByLabel("Treść opinii").fill(text);
    await this.page.getByRole("button", { name: "Wyślij opinię" }).click();
  }
}
