import { expect, type Locator, type Page } from "@playwright/test";
import { awaitCircuitIfInteractive, gotoInteractive, watchCircuit } from "./circuit";
import { bookingIdFromUrl } from "./booking-api";

/** Page objects of the booking and review flow (J3, J4). Role and label locators; texts are the Polish strings of web/src/components/{booking,bookings,reviews,profile}/strings.ts. */
export class PractitionerProfilePage {
  constructor(readonly page: Page) {}
  /**
   * The profile page is rendered by the web app from the Directory, which limits each client address to 60 requests a minute (shared with the whole run),
   * so the page can be an error page for a moment. Reload until the h1 is there (bounded); on failure say what the page showed (title and first heading only).
   */
  async goto(slug: string, displayName: string) {
    const h1 = this.page.getByRole("heading", { level: 1, name: displayName });
    await gotoInteractive(this.page, `/gabinet/${slug}`);
    try {
      await expect(async () => {
        if (!(await h1.isVisible())) await this.page.reload();
        await expect(h1).toBeVisible({ timeout: 5_000 });
      }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
    } catch (err) {
      const first = await this.page.getByRole("heading").first().textContent({ timeout: 2_000 }).catch(() => null);
      throw new Error(`profile page of ${slug} did not render: title "${await this.page.title()}", first heading "${first ?? ""}"`, { cause: err });
    }
  }
  /** The offering's card: the one list item that holds the offering's heading (the time list inside it has no heading). */
  offering(title: string): Locator {
    return this.page.getByRole("listitem").filter({ has: this.page.getByRole("heading", { level: 3, name: title }) });
  }
  slotLinks(offeringTitle: string): Locator { return this.offering(offeringTitle).getByRole("link", { name: /^\d{2}:\d{2}$/ }); }
  /**
   * The first free time of the offering. The picker reads the slots through the BFF after the page has loaded, so a rate limited or slow answer shows
   * "Nie możemy teraz pokazać terminów" or no times at first: reload until a time is there (bounded); say what the section showed otherwise.
   */
  async firstSlot(offeringTitle: string): Promise<Locator> {
    const first = this.slotLinks(offeringTitle).first();
    try {
      await expect(async () => {
        if (!(await first.isVisible())) await this.page.reload();
        await expect(first).toBeVisible({ timeout: 8_000 });
      }).toPass({ timeout: 90_000, intervals: [2_000, 5_000, 10_000] });
    } catch (err) {
      const shown = await this.offering(offeringTitle).locator("p").allTextContents().catch(() => []);
      throw new Error(`no free time shown for "${offeringTitle}"; the card said: ${shown.map((t) => t.slice(0, 80)).join(" | ")}`, { cause: err });
    }
    return first;
  }
  /** Clicks a free time and waits for the booking form. If the host renders it interactively, nothing typed before its circuit is live would count, so the circuit is awaited. */
  async openBookingForm(slot: Locator) {
    const circuit = watchCircuit(this.page);
    const answer = this.page.waitForResponse((r) => ["document", "fetch", "xhr"].includes(r.request().resourceType()) && /\/rezerwacja\/[0-9a-f-]{36}/.test(new URL(r.url()).pathname));
    await slot.click();
    const res = await answer;
    await this.page.waitForURL(/\/rezerwacja\//);
    await awaitCircuitIfInteractive(res, circuit); // enhanced navigation answers a fetch, not a document; a static form has no circuit, an interactive one is awaited
  }
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
    await gotoInteractive(this.page, `/moje/opinie/${bookingId}`);
    await expect(this.page.getByRole("button", { name: "Wyślij opinię" })).toBeVisible();
  }
  async submit(rating: number, text: string) {
    await this.page.getByRole("radio", { name: `${rating} z 5` }).check();
    await this.page.getByLabel("Treść opinii").fill(text);
    await this.page.getByRole("button", { name: "Wyślij opinię" }).click();
  }
}
