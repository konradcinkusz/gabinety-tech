import { expect, type APIRequestContext, type BrowserContext, type Page } from "@playwright/test";
import { demoPassword, emailOf, urls } from "./env";
import { fetchRetrying } from "./http";

/**
 * Signs in as a seeded demo user (DevIdentity, fixed development password) through the real sign-in form, so the session cookie is set exactly as for a person.
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
 * Faster: posts the same request the form does with the context's cookie jar, so the context is signed in without rendering the form.
 * Use it in `beforeEach` of journeys that are not about signing in.
 */
export async function signInViaBff(context: BrowserContext, userName: string): Promise<void> {
  tokens.set(context.request, await identityToken(userName)); // for the journeys' API set-up and verification (below)
  return signInViaFormPost(context, userName); // the host has no login API: the browser never calls a backend
}

/**
 * The request the static sign-in form makes, with the context's cookie jar: GET /logowanie for the antiforgery token (and its cookie), then POST the
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
  await context.clearCookies({ name: "gt_session" }); // the server-side revoke is /wyloguj (a form); journeys that test it use the page
}

// ---- The host has no `/api/proxy/<service>/...` (the browser never calls a backend), so the journeys set up and verify by going straight to the
// service with the user's access token instead, kept per context here (booking-support.ts `bff`, booking-api.ts). A development token of the fake profile's DevIdentity (synthetic
// `demo-` users); never logged. What a journey is ABOUT still goes through the UI.
const tokens = new WeakMap<object, string>();
const IDENTITY_CONSENT_VERSION = "2026-01-01"; // DevIdentity's and authservice's default consent version (tools/Gabinety.DevIdentity/Endpoints.cs)

/** The bearer token of a context (`context.request`) signed in with `signInViaBff` (undefined for one that never signed in). */
export const accessTokenOf = (request: APIRequestContext): string | undefined => tokens.get(request);

async function identityToken(userName: string): Promise<string> {
  const res = await fetchRetrying(`${urls.devidentity}/api/v1/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: emailOf(userName), password: demoPassword }),
  });
  expect(res.status, `identity login as ${userName}`).toBe(200); // the status only: never the body or the token
  return ((await res.json()) as { accessToken: string }).accessToken;
}

/** Registers `userName` at the identity service directly (the host has no registration API; the registration page is what a journey drives). */
export async function registerAtIdentity(userName: string): Promise<void> {
  const res = await fetchRetrying(`${urls.devidentity}/api/v1/auth/register`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: emailOf(userName), password: demoPassword, acceptedTermsVersion: IDENTITY_CONSENT_VERSION, acceptedPrivacyVersion: IDENTITY_CONSENT_VERSION }),
  });
  expect(res.status, `register ${userName}`).toBe(200);
}
