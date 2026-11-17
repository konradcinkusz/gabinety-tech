import { expect, type Locator, type Page } from "@playwright/test";

/** Page helpers: role and label first (README). One class per page a journey touches; add yours next to these, do not inline selectors in specs. */
export class HomePage {
  constructor(readonly page: Page) {}
  goto() { return this.page.goto("/"); }
  heading(): Locator { return this.page.getByRole("heading", { level: 1 }); }
}

export class PractitionerListPage {
  constructor(readonly page: Page) {}
  async goto(query = "") {
    await this.page.goto(`/programisci${query}`);
    await expect(this.heading()).toBeVisible();
  }
  heading(): Locator { return this.page.getByRole("heading", { level: 1 }); }
  /** One entry per practitioner card (the cards hold lists of their own, so only the direct children of the results list). */
  results(): Locator { return this.page.getByRole("region", { name: "Lista programistów" }).getByRole("list").first().locator(":scope > li"); }
  /** The link of one practitioner card, by the displayed name. */
  profileLink(displayName: string): Locator { return this.page.getByRole("link", { name: displayName, exact: true }); }
  async open(displayName: string) {
    await this.profileLink(displayName).click();
    await expect(this.page.getByRole("heading", { level: 1, name: displayName })).toBeVisible();
  }
}
