import { expect, type BrowserContext, type Page } from "@playwright/test";
import { demoPassword, emailOf } from "./env";

/**
 * Signs in as a seeded demo user (DevIdentity, fixed development password) through the real sign-in form, so the BFF cookie is set exactly as for a person.
 * Role and label locators only (README). The page ends on `returnTo` (default `/`).
 */
export async function signInThroughForm(page: Page, userName: string, returnTo = "/"): Promise<void> {
  await page.goto(`/logowanie?redirect=${encodeURIComponent(returnTo)}`);
  await page.getByLabel("Adres e-mail").fill(emailOf(userName));
  await page.getByLabel("Hasło").fill(demoPassword);
  await page.getByRole("button", { name: "Zaloguj się" }).click();
  await expect(page).not.toHaveURL(/\/logowanie/);
}

/**
 * Faster: posts the same request the form does to the BFF with the context's cookie jar, so the context is signed in without rendering the form.
 * Use it in `beforeEach` of journeys that are not about signing in.
 */
export async function signInViaBff(context: BrowserContext, userName: string): Promise<void> {
  const res = await context.request.post("/api/auth/login", { data: { email: emailOf(userName), password: demoPassword } });
  expect(res.status(), `sign-in as ${userName}`).toBe(200); // the status only: never the body or the token
}

export async function signOut(context: BrowserContext): Promise<void> {
  await context.request.post("/api/auth/logout");
}
