import { expect, type BrowserContext, type Page } from "@playwright/test";
import { demoPassword, emailOf } from "./env";
import { isBlazor } from "./host";

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
  if (isBlazor) return signInViaFormPost(context, userName); // the Blazor host has no /api/auth/login: the browser never calls a backend
  const res = await context.request.post("/api/auth/login", { data: { email: emailOf(userName), password: demoPassword } });
  expect(res.status(), `sign-in as ${userName}`).toBe(200); // the status only: never the body or the token
}

/**
 * HOST=blazor: the request the static sign-in form makes, with the context's cookie jar: GET /logowanie for the antiforgery token (and its cookie), then POST the
 * form (handler, token, e-mail, password). A 302 with the session cookie set is the success; the status only is asserted, never a body.
 */
export async function signInViaFormPost(context: BrowserContext, userName: string): Promise<void> {
  const page = await context.request.get("/logowanie");
  expect(page.status(), "GET /logowanie").toBe(200);
  const html = await page.text();
  const token = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(html)?.[1];
  const handler = /name="_handler"[^>]*value="([^"]+)"/.exec(html)?.[1] ?? "login";
  expect(token, "antiforgery token on /logowanie").toBeTruthy();
  const res = await context.request.post("/logowanie", {
    form: { _handler: handler, __RequestVerificationToken: token!, "Model.Email": emailOf(userName), "Model.Password": demoPassword },
    maxRedirects: 0,
  });
  expect([302, 303], `sign-in as ${userName}`).toContain(res.status());
  expect((await context.cookies()).some((c) => c.name === "gt_session"), "session cookie").toBe(true);
}

export async function signOut(context: BrowserContext): Promise<void> {
  if (isBlazor) { await context.clearCookies({ name: "gt_session" }); return; } // the server-side revoke is /wyloguj (a form); journeys that test it use the page
  await context.request.post("/api/auth/logout");
}
