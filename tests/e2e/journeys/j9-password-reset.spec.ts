import { expect, test } from "../harness/fixtures";
import { expectNoSeriousA11yViolations } from "../harness/a11y";
import { demoPassword, emailOf, urls } from "../harness/env";
import { registerAtIdentity } from "../harness/auth";
import { fetchRetrying } from "../harness/http";
import { uniqueDemoName } from "../harness/booking-support";
import type { Page } from "@playwright/test";

// J9 (T-11.20): password recovery end to end in the fake profile. The fake identity service sends no e-mail, so the reset link's token is read from its development route
// (`GET /dev/last-reset-token?email=`, mapped only under GABINETY_FAKE=1; tools/Gabinety.DevIdentity). Everything the person does goes through the pages: ask for the link,
// open it, set the new password, sign in with it; the old password is refused, the link works once, and the answer to "forgot" never says whether the address exists.
// Each test registers its own `demo-` user, so no reseed is needed and the clock is never moved.
const NEW_PASSWORD = "Nowe-Haslo-Test1!"; // a fixture of the fake profile (low entropy, meets the rules), not a credential

async function lastResetToken(email: string): Promise<string | null> {
  const res = await fetchRetrying(`${urls.devidentity}/dev/last-reset-token?email=${encodeURIComponent(email)}`);
  if (res.status === 404) return null;
  expect(res.status, "dev route of the last reset token").toBe(200);
  return ((await res.json()) as { token: string }).token;
}

async function askForLink(page: Page, email: string) {
  await page.goto("/zapomniane-haslo");
  await expect(page.getByRole("heading", { level: 1, name: "Nie pamiętasz hasła?" })).toBeVisible();
  await page.getByLabel("Adres e-mail").fill(email);
  await page.getByRole("button", { name: "Wyślij link" }).click();
}

async function setNewPassword(page: Page, link: string, password: string) {
  await page.goto(link);
  await expect(page.getByRole("heading", { level: 1, name: "Ustaw nowe hasło" })).toBeVisible();
  await page.getByLabel("Nowe hasło", { exact: true }).fill(password);
  await page.getByLabel("Powtórz nowe hasło", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Zmień hasło" }).click();
}

async function signIn(page: Page, email: string, password: string) {
  await page.goto("/logowanie");
  await page.getByLabel("Adres e-mail").fill(email);
  await page.getByLabel("Hasło", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Zaloguj się" }).click();
}

test.describe("J9 password reset", () => {
  const ANSWER = "Jeśli konto o tym adresie istnieje, wysłaliśmy wiadomość z linkiem.";

  test("a user resets the password from the mailed link and signs in with the new one only", async ({ page }) => {
    const userName = uniqueDemoName("j9-reset");
    const email = emailOf(userName);
    await registerAtIdentity(userName);

    await askForLink(page, email);
    await expect(page.getByText(ANSWER)).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    const resetKey = await lastResetToken(email);
    expect(resetKey, "the fake identity issued a reset link for the registered address").toBeTruthy();
    const link = `/reset-password?token=${encodeURIComponent(resetKey!)}&email=${encodeURIComponent(email)}`;

    await page.goto(link);
    await expect(page.getByRole("heading", { level: 1, name: "Ustaw nowe hasło" })).toBeVisible();
    await expectNoSeriousA11yViolations(page);
    await setNewPassword(page, link, NEW_PASSWORD);

    // the link led to the sign-in page with its flash; nothing signed the person in on its own
    await expect(page).toHaveURL(/\/logowanie/);
    await expect(page.getByText("Hasło zostało zmienione. Zaloguj się.")).toBeVisible();
    await page.goto("/konto");
    await expect(page).toHaveURL(/\/logowanie/);

    // the old password is refused ...
    await signIn(page, email, demoPassword);
    await expect(page.getByText("Nieprawidłowy adres e-mail lub hasło.")).toBeVisible();
    await expect(page).toHaveURL(/\/logowanie/);

    // ... and the new one signs in: the account page shows the address
    await signIn(page, email, NEW_PASSWORD);
    await expect(page).not.toHaveURL(/\/logowanie/);
    await page.goto("/konto");
    await expect(page.getByRole("heading", { level: 1, name: "Konto" })).toBeVisible();
    await expect(page.getByText(email).first()).toBeVisible(); // the address is shown twice (the header and the account card)
  });

  test("the link works once: a second use is refused and the first password stays", async ({ page }) => {
    const userName = uniqueDemoName("j9-once");
    const email = emailOf(userName);
    await registerAtIdentity(userName);
    await askForLink(page, email);
    await expect(page.getByText(ANSWER)).toBeVisible();
    const resetKey = await lastResetToken(email);
    expect(resetKey).toBeTruthy();
    const link = `/reset-password?token=${encodeURIComponent(resetKey!)}&email=${encodeURIComponent(email)}`;

    await setNewPassword(page, link, NEW_PASSWORD);
    await expect(page.getByText("Hasło zostało zmienione. Zaloguj się.")).toBeVisible();

    // the same link again, even with a different password
    await setNewPassword(page, link, "Inne-Haslo-Test2!");
    await expect(page.getByText("Link do zmiany hasła jest nieprawidłowy, został już użyty albo wygasł.")).toBeVisible();
    await expect(page.getByRole("link", { name: "Poproś o nowy link" })).toBeVisible();
    await expectNoSeriousA11yViolations(page);

    // the second attempt changed nothing: the password of the first one signs in, the other does not
    await signIn(page, email, "Inne-Haslo-Test2!");
    await expect(page.getByText("Nieprawidłowy adres e-mail lub hasło.")).toBeVisible();
    await signIn(page, email, NEW_PASSWORD);
    await expect(page).not.toHaveURL(/\/logowanie/);
  });

  test("asking for a link never reveals whether the address has an account", async ({ page }) => {
    const knownName = uniqueDemoName("j9-known");
    const known = emailOf(knownName);
    const unknown = emailOf(uniqueDemoName("j9-nobody"));
    await registerAtIdentity(knownName);

    await askForLink(page, known);
    const answerKnown = page.getByText(ANSWER);
    await expect(answerKnown).toBeVisible();
    const textKnown = await page.getByRole("main").innerText();

    await askForLink(page, unknown);
    await expect(page.getByText(ANSWER)).toBeVisible();
    const textUnknown = await page.getByRole("main").innerText();

    expect(textUnknown, "the page for an unknown address reads exactly as for a known one").toBe(textKnown);
    // the control that proves the two cases really differ behind the scenes: only the known address got a link
    expect(await lastResetToken(known)).toBeTruthy();
    expect(await lastResetToken(unknown)).toBeNull();
  });
});
