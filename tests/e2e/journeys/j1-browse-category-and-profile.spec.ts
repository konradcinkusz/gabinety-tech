import { urls } from "../harness/env";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { expect, test } from "../harness/fixtures";
import { PractitionerListPage } from "../harness/pages";
import { listedInProblem, publicProfile, taxonomy } from "./support/directory-api";
import { CategoryNav, PublicProfilePage } from "./support/pages";

// J1 (SPEC 10): a visitor browses a category, opens a profile, and the page content is present with JavaScript disabled.
// Read-only: it writes nothing, so it needs no demo reset, and it does not depend on any data another journey creates
// (the category and practitioner are taken from what the Directory lists now).
test.describe("J1 visitor browses a category and opens a profile", () => {
  test("the category lists a practitioner, the profile opens, and its content is in the HTML without JavaScript", async ({ page, browser }) => {
    // The category to browse: the first problem that has a listed practitioner, and one practitioner of it (the Directory's view, not a hard-coded name).
    const { problems } = await taxonomy();
    let category: { slug: string; namePl: string } | undefined;
    let expected: { slug: string; displayName: string } | undefined;
    for (const c of problems) {
      const listed = await listedInProblem(c.slug);
      if (listed.length > 0) { category = c; expected = listed[0]; break; }
    }
    expect(category, "a seeded problem category with a listed practitioner").toBeDefined();
    const profile = await publicProfile(expected!.slug);
    expect(profile, "the listed practitioner has a public profile").not.toBeNull();
    expect(profile!.offerings.length).toBeGreaterThan(0);

    await test.step("browse: all practitioners, then the category through the filter link", async () => {
      const list = new PractitionerListPage(page);
      await list.goto();
      await expectNoSeriousA11yViolations(page);
      await new CategoryNav(page).link(category!.namePl).click();
      await expect(page).toHaveURL(new RegExp(`/programisci/${category!.slug}`));
      await expect(list.heading()).toHaveText(category!.namePl);
      await expect(page.getByText(/^Wyniki: \d+$/)).toBeVisible();
      await expect(list.profileLink(expected!.displayName)).toBeVisible();
      await expectNoSeriousA11yViolations(page);
    });

    await test.step("open the profile from the card; it shows what the Directory holds", async () => {
      await new PractitionerListPage(page).open(expected!.displayName);
      await expect(page).toHaveURL(new RegExp(`/gabinet/${expected!.slug}$`));
      const view = new PublicProfilePage(page);
      await expect(page.getByText(profile!.headline)).toBeVisible();
      await expect(page.getByRole("heading", { level: 2, name: "O mnie" })).toBeVisible();
      for (const o of profile!.offerings) await expect(view.bookLink(o.title)).toBeVisible();
      await expectNoSeriousA11yViolations(page);
    });

    await test.step("the same profile in a context with JavaScript disabled still has its content", async () => {
      // A second context: `browser.newContext` does not inherit the project's `use`, so the base URL and locale are passed.
      const noJs = await browser.newContext({ javaScriptEnabled: false, baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
      try {
        const p = await noJs.newPage();
        const res = await p.goto(`/gabinet/${expected!.slug}`);
        expect(res?.status()).toBe(200);
        const view = new PublicProfilePage(p);
        await expect(view.heading(profile!.displayName)).toBeVisible();
        await expect(p.getByText(profile!.headline)).toBeVisible();
        await expect(p.getByRole("heading", { level: 2, name: "O mnie" })).toBeVisible();
        await expect(p.getByText(profile!.bio)).toBeVisible();
        await expect(p.getByRole("heading", { level: 2, name: "Oferta" })).toBeVisible();
        for (const o of profile!.offerings) {
          await expect(p.getByRole("heading", { level: 3, name: o.title })).toBeVisible();
          await expect(p.getByText(o.description)).toBeVisible();
          // booking works without scripts: a plain link to the booking page of that offering (the page adds `?gabinet=<slug>` to come back to)
          await expect(view.bookLink(o.title)).toHaveAttribute("href", new RegExp(`^/rezerwacja/${o.id}(\\?.*)?$`));
        }
        // structured data for search engines is in the document too
        await expect(p.locator('script[type="application/ld+json"]')).toHaveCount(1);
        expect(await p.locator('script[type="application/ld+json"]').textContent()).toContain(profile!.displayName);
      } finally {
        await noJs.close();
      }
    });
  });
});
