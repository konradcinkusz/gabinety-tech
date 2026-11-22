import { randomBytes } from "node:crypto";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { accessTokenOf } from "../harness/auth";
import { emailOf, urls } from "../harness/env";
import { expect, test } from "../harness/fixtures";
import { actorContext, registerDemoUser, uniqueDemoName } from "../harness/booking-support";
import { fetchRetrying } from "../harness/http";

// J8 (ADR proposal 0013): a programmer advertises contract availability, a client sends a long-term proposal, the programmer answers with other terms, the client accepts, and each side
// then sees the other's e-mail. The journey is about the UI; the follow-up state (the proposal's status) is read from Booking's API with the client's own token.
// Everything it creates is in the `demo-` namespace under a unique suffix (the client account, the company name), so it neither depends on nor disturbs another journey.
// The programmer is the seeded demo-practitioner-6 (Filip Syntetyczny, Approved); the seeder gives him no contract availability, so the journey switches it on itself.
test.describe("J8 collaboration proposal: advertise, propose, counter, accept", () => {
  const PROGRAMMER = "demo-practitioner-6";
  const SLUG = "demo-filip-syntetyczny";
  const isoDay = (daysAhead: number) => new Date(Date.now() + daysAhead * 86_400_000).toISOString().slice(0, 10);

  test("from availability to an accepted proposal with both addresses", async ({ page, context, browser }) => {
    test.setTimeout(240_000);
    const uid = randomBytes(4).toString("hex");
    const clientName = uniqueDemoName("j8");
    const company = `Demo Firma ${uid}`;
    const message = "Szukamy programisty do dłuższej współpracy nad naszym systemem rezerwacji. Potrzebujemy kilkunastu godzin tygodniowo przez pół roku.";

    const programmer = await actorContext(browser, PROGRAMMER);
    const programmerPage = await programmer.newPage();
    let proposalUrl = "";
    let proposalId = "";

    try {
      await test.step("the programmer switches contract availability on in the practitioner area", async () => {
        await programmerPage.goto("/gabinet-programisty/profil");
        await programmerPage.getByLabel("Dostępność", { exact: true }).selectOption("available_now");
        await programmerPage.getByLabel("Godzin tygodniowo").fill("15");
        await programmerPage.getByLabel("Orientacyjna stawka").fill("150");
        await programmerPage.getByRole("button", { name: "Ustaw dostępność" }).click();
        await expect(programmerPage.getByText("Zapisano.")).toBeVisible();
        await expect(programmerPage.getByLabel("Godzin tygodniowo")).toHaveValue("15");
        await expectNoSeriousA11yViolations(programmerPage);
      });

      await test.step("a client sees the availability on the public profile and opens the long-term form", async () => {
        await registerDemoUser(context, clientName);
        await page.goto(`/gabinet/${SLUG}`);
        await expect(page.getByRole("heading", { level: 2, name: "Współpraca kontraktowa" })).toBeVisible();
        await expect(page.getByText("Dostępny do współpracy od zaraz")).toBeVisible();
        await expect(page.getByText("15 godz. tygodniowo")).toBeVisible();
        await expectNoSeriousA11yViolations(page);
        await page.getByRole("link", { name: "Zaproponuj współpracę długoterminową" }).click();
        await expect(page).toHaveURL(new RegExp(`/propozycja/${SLUG}$`));
        await expect(page.getByRole("heading", { level: 1 })).toContainText("Propozycja współpracy długoterminowej dla");
      });

      await test.step("the form says every missing field at once, and then the service refuses an e-mail address in the message, both in our words", async () => {
        await page.getByLabel("Wiadomość do programisty").fill("Napisz do mnie na jan@firma.example w sprawie współpracy.");
        await page.getByRole("button", { name: "Wyślij propozycję" }).click();
        const summary = page.locator("[data-testid=form-summary]");
        await expect(summary).toBeVisible();
        await expect(summary.getByRole("link")).toHaveCount(4); // rate, hours a week, months, start: our own check, nothing was sent
        await expectNoSeriousA11yViolations(page);

        await page.getByLabel("Stawka za godzinę").fill("150");
        await page.getByLabel("Godzin tygodniowo").fill("20");
        await page.getByLabel("Okres (miesiące)").fill("6");
        await page.getByLabel("Start", { exact: true }).fill(isoDay(14));
        await page.getByRole("button", { name: "Wyślij propozycję" }).click();
        await expect(summary.getByRole("link")).toHaveCount(1); // the service refuses the address: only the message is wrong
        await expect(page.locator("#text-error")).toContainText("Usuń adresy e-mail");
        await expect(page.getByLabel("Stawka za godzinę")).toHaveValue("150"); // what was typed stays
      });

      await test.step("the client sends the proposal", async () => {
        await page.getByLabel("Wiadomość do programisty").fill(message);
        await page.getByLabel("Nazwa firmy").fill(company);
        await page.getByRole("button", { name: "Wyślij propozycję" }).click();
        await expect(page).toHaveURL(/\/moje\/propozycje\/[0-9a-f-]{36}\?done=sent$/);
        proposalUrl = new URL(page.url()).pathname;
        proposalId = proposalUrl.split("/").pop()!;
        await expect(page.getByText("Propozycja została wysłana. Programista dostanie powiadomienie.")).toBeVisible();
        await expect(page.getByText("Czeka na programistę")).toBeVisible();
        await expect(page.getByText(/150,00\s*zł za godzinę/).first()).toBeVisible();
        await expect(page.getByText(company)).toBeVisible();
        await expect(page.getByRole("heading", { name: "Dane kontaktowe" })).toHaveCount(0); // not accepted yet: no address
        await expect(page.getByRole("button", { name: "Wycofaj propozycję" })).toBeVisible();
        await expectNoSeriousA11yViolations(page);
        await page.goto("/moje/propozycje");
        await expect(page.getByRole("listitem").filter({ hasText: "Czekasz na odpowiedź" }).first()).toBeVisible();
      });

      await test.step("the programmer finds it in the inbox, with whose turn it is, and answers with other terms", async () => {
        await programmerPage.goto("/gabinet-programisty/propozycje");
        const row = programmerPage.getByRole("listitem").filter({ hasText: company });
        await expect(row).toContainText("Twoja kolej");
        await row.getByRole("link", { name: /Szczegóły/ }).click();
        await expect(programmerPage.getByText("To Twoja kolej")).toBeVisible();
        await expectNoSeriousA11yViolations(programmerPage);
        await programmerPage.getByRole("button", { name: "Zaproponuj inne warunki" }).click();
        await programmerPage.getByLabel("Stawka za godzinę").fill("140");
        await programmerPage.getByLabel("Godzin tygodniowo").fill("10");
        await programmerPage.getByLabel("Wiadomość do drugiej strony").fill("Proponuję niższą stawkę przy mniejszym wymiarze, bo tyle realnie mogę poświęcić.");
        await programmerPage.getByRole("button", { name: "Wyślij kontrpropozycję" }).click();
        await expect(programmerPage.getByText("Kontrpropozycja została wysłana.")).toBeVisible();
        await expect(programmerPage.getByText("Czeka na klienta")).toBeVisible();
        await expect(programmerPage.getByText("Runda 2: Kontrpropozycja (programista)")).toBeVisible();
      });

      await test.step("the client sees the counter-proposal and accepts it", async () => {
        await page.goto(proposalUrl);
        await expect(page.getByText("Czeka na klienta")).toBeVisible();
        await expect(page.getByText("To Twoja kolej")).toBeVisible();
        await expect(page.getByText(/140,00\s*zł za godzinę/).first()).toBeVisible();
        await expect(page.getByText("Runda 2: Kontrpropozycja (programista)")).toBeVisible();
        await page.getByRole("button", { name: "Przyjmij warunki" }).click();
        await expect(page.getByRole("alertdialog")).toContainText("Przyjąć te warunki?");
        await page.getByRole("button", { name: "Tak, przyjmuję" }).click();
        await expect(page.getByText("Zaakceptowana").first()).toBeVisible();
        await expect(page.getByRole("heading", { name: "Dane kontaktowe" })).toBeVisible();
        await expect(page.getByRole("link", { name: emailOf(PROGRAMMER) })).toBeVisible();
        await expect(page.getByRole("button", { name: "Przyjmij warunki" })).toHaveCount(0); // closed: nothing more to do
        await expectNoSeriousA11yViolations(page);
      });

      await test.step("the programmer sees the client's address too", async () => {
        await programmerPage.reload();
        await expect(programmerPage.getByText("Zaakceptowana").first()).toBeVisible();
        await expect(programmerPage.getByRole("heading", { name: "Dane kontaktowe" })).toBeVisible();
        await expect(programmerPage.getByRole("link", { name: emailOf(clientName) })).toBeVisible();
      });

      await test.step("the follow-up state: Booking holds an accepted proposal with three rounds, and a stranger gets 404", async () => {
        const token = accessTokenOf(context.request);
        expect(token, "the client's token").toBeTruthy();
        const view = await (await fetchRetrying(`${urls.booking}/api/v1/proposals/${proposalId}`, { headers: { Authorization: `Bearer ${token}` } })).json();
        expect(view.status).toBe("Accepted");
        expect(view.rounds.map((r: { action: string }) => r.action)).toEqual(["Proposed", "Countered", "Accepted"]);
        expect(view.terms.rate.amountMinor).toBe(14000);
        expect(view.terms.hoursPerWeek).toBe(10);

        const stranger = await browser.newContext({ baseURL: urls.web, locale: "pl-PL", timezoneId: "Europe/Warsaw" });
        try {
          await registerDemoUser(stranger, uniqueDemoName("j8-x"));
          expect((await stranger.request.get(proposalUrl)).status()).toBe(404);
        } finally {
          await stranger.close();
        }
      });
    } finally {
      await programmer.close();
    }
  });
});
