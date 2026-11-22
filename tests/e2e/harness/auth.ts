import { expect, type APIRequestContext, type BrowserContext, type Page } from "@playwright/test";
import { demoPassword, emailOf, isBlazor, urls } from "./env";
import { fetchRetrying } from "./http";

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
  if (isBlazor) return signInBlazor(context, userName);
  const res = await context.request.post("/api/auth/login", { data: { email: emailOf(userName), password: demoPassword } });
  expect(res.status(), `sign-in as ${userName}`).toBe(200); // the status only: never the body or the token
}

export async function signOut(context: BrowserContext): Promise<void> {
  await context.request.post("/api/auth/logout");
}

// ---- HOST=blazor (ADR-0010): there is no BFF. The browser context is signed in through the real form (the host sets its own cookie); API set-up and
// verification that the Next journeys did through `/api/proxy/...` go straight to the backend with the user's access token, kept per context here.
// A token held by the test is a development token of the fake profile's DevIdentity (synthetic `demo-` users); it is never logged.

const tokens = new WeakMap<object, string>();
const IDENTITY_CONSENT_VERSION = "2026-01-01"; // DevIdentity's and authservice's default consent version (tools/Gabinety.DevIdentity/Endpoints.cs)

/** The bearer token of a context (`context.request`) signed in with `signInViaBff` on the Blazor host (undefined for one that never signed in). */
export const accessTokenOf = (request: APIRequestContext): string | undefined => tokens.get(request);

async function identityToken(userName: string): Promise<string> {
  const res = await fetchRetrying(`${urls.devidentity}/api/v1/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ email: emailOf(userName), password: demoPassword }),
  });
  expect(res.status, `identity login as ${userName}`).toBe(200); // the status only: never the body or the token
  return ((await res.json()) as { accessToken: string }).accessToken;
}

async function signInBlazor(context: BrowserContext, userName: string): Promise<void> {
  tokens.set(context.request, await identityToken(userName));
  const page = await context.newPage();
  try {
    await signInThroughForm(page, userName);
  } finally {
    await page.close();
  }
}

/** Registers `userName` at the identity service directly (the Blazor host has no /api/auth/register; the registration page is port/auth's). */
export async function registerAtIdentity(userName: string): Promise<void> {
  const res = await fetchRetrying(`${urls.devidentity}/api/v1/auth/register`, {
    method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email: emailOf(userName), password: demoPassword, acceptedTermsVersion: IDENTITY_CONSENT_VERSION, acceptedPrivacyVersion: IDENTITY_CONSENT_VERSION }),
  });
  expect(res.status, `register ${userName}`).toBe(200);
}
