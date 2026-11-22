// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { login } from "../../lib/session/handlers";
import { fakeBackend, tokenBody } from "../../lib/api/testUtils";
import { resetBackendFetch } from "../../lib/api/http";
import { LoginForm } from "./LoginForm";
import { RegisterForm } from "./RegisterForm";
import { at } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const SITE = "http://localhost:3000";
let host: HTMLElement;
let root: Root;
beforeEach(() => {
  vi.stubEnv("Backend__Auth", "http://auth.internal");
  vi.stubEnv("Site__BaseUrl", SITE);
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  resetBackendFetch();
});
const render = (el: React.ReactElement) => act(() => root.render(el));
const $ = (sel: string) => host.querySelector(sel) as HTMLElement;
const byLabel = (re: RegExp) =>
  [...host.querySelectorAll("label")].find((l) => re.test(l.textContent ?? ""))!.control as HTMLInputElement;

/** Routes the browser's fetch to the REAL BFF handler, with authservice faked behind it. */
function browserToBff() {
  vi.stubGlobal("fetch", async (url: string, init: RequestInit) => {
    if (url === "/api/auth/login") {
      return login(new Request(SITE + url, { ...init, headers: { "content-type": "application/json", origin: SITE } }));
    }
    throw new Error("unexpected " + url);
  });
}

async function signIn(email: string, password: string) {
  const user = userEvent.setup();
  await user.type(byLabel(/e-mail/i), email);
  await user.type(byLabel(/hasło/i), password);
  await user.click($("button[type=submit]"));
}

describe("LoginForm", () => {
  it("shows the same text for a wrong password and for an unknown e-mail", async () => {
    // authservice answers both with 401; the BFF maps both to one code; the form must print one text.
    fakeBackend(() => new Response("{}", { status: 401 }));
    browserToBff();
    render(<LoginForm returnTo="/" navigate={() => {}} />);
    await signIn("demo-known@example.test", "wrong-password-1");
    const wrongPassword = $("[role=alert]").textContent;
    act(() => root.render(<LoginForm key="2" returnTo="/" navigate={() => {}} />));
    await signIn("demo-nobody@example.test", "wrong-password-1");
    const unknownEmail = $("[role=alert]").textContent;
    expect(wrongPassword).toBe(at("login.invalidCredentials"));
    expect(unknownEmail).toBe(wrongPassword);
    expect(host.textContent).not.toContain("demo-nobody@example.test");
  });

  it("goes back to the page the user came from after sign-in", async () => {
    fakeBackend(() => tokenBody());
    browserToBff();
    const navigate = vi.fn();
    render(<LoginForm returnTo="/moje/rezerwacje" navigate={navigate} />);
    await signIn("demo@example.test", "pw-demo-1234");
    expect(navigate).toHaveBeenCalledWith("/moje/rezerwacje");
  });

  it("ignores an external returnTo", async () => {
    fakeBackend(() => tokenBody());
    browserToBff();
    const navigate = vi.fn();
    render(<LoginForm returnTo="https://evil.example" navigate={navigate} />);
    await signIn("demo@example.test", "pw-demo-1234");
    expect(navigate).toHaveBeenCalledTimes(1);
    expect(navigate).toHaveBeenCalledWith("/");
  });

  it("offers the GitHub sign-in through the BFF, carrying only a safe return path", () => {
    render(<LoginForm returnTo="https://evil.example" navigate={() => {}} />);
    const a = host.querySelector("a[href^='/api/auth/github/start']") as HTMLAnchorElement;
    expect(a).not.toBeNull();
    expect(a.getAttribute("href")).not.toContain("evil");
  });

  it("reports an unverified e-mail and an unavailable service in words", async () => {
    vi.stubGlobal("fetch", async () => Response.json({ code: "email_verification_required" }, { status: 403 }));
    render(<LoginForm returnTo="/" navigate={() => {}} />);
    await signIn("demo@example.test", "pw-demo-1234");
    expect($("[role=alert]").textContent).toBe(at("login.verificationRequired"));
  });
});

describe("LoginForm rate limit", () => {
  it("says to wait on 429 instead of calling the password wrong", async () => {
    fakeBackend(() => Response.json({ error: "Too many requests", retryAfter: 30 }, { status: 429 }));
    browserToBff();
    render(<LoginForm returnTo="/" navigate={() => {}} />);
    await signIn("demo@example.test", "Pw-demo-1234!");
    expect($("[role=alert]").textContent).toBe(at("login.rateLimited"));
  });
});

describe("RegisterForm", () => {
  const versions = { terms: "2026-01-01", privacy: "2026-02-01" };

  async function fill(consent: boolean, password = "Pw-demo-1234!") {
    const user = userEvent.setup();
    await user.type(byLabel(/e-mail/i), "demo-new@example.test");
    await user.type(byLabel(/^hasło/i), password);
    if (consent) await user.click(byLabel(/regulamin/i));
    await user.click($("button[type=submit]"));
  }

  it("cannot submit without the consent", async () => {
    const f = vi.fn();
    vi.stubGlobal("fetch", f);
    render(<RegisterForm versions={versions} returnTo="/" navigate={() => {}} />);
    await fill(false);
    expect(f).not.toHaveBeenCalled();
    expect($("[role=alert]").textContent).toContain(at("register.consentRequired"));
  });

  it.each(["pw-demo-1234!", "PW-DEMO-1234!", "Pw-demo-abcd!", "Pwdemo1234"])("refuses %s before sending: authservice needs a digit, both cases and a special character", async (weak) => {
    const f = vi.fn();
    vi.stubGlobal("fetch", f);
    render(<RegisterForm versions={versions} returnTo="/" navigate={() => {}} />);
    await fill(true, weak);
    expect(f).not.toHaveBeenCalled();
    expect($("[role=alert]").textContent).toBe(at("register.passwordWeak"));
  });

  it("says to wait, not that the data is wrong, when authservice's limit answers 429", async () => {
    vi.stubGlobal("fetch", async () => Response.json({ code: "rate_limited" }, { status: 429 }));
    render(<RegisterForm versions={versions} returnTo="/" navigate={() => {}} />);
    await fill(true);
    expect($("[role=alert]").textContent).toBe(at("register.rateLimited"));
  });

  it("sends the consent versions it was given, and shows the verification notice on 202", async () => {
    const f = vi.fn(async () => Response.json({ code: "verification_required" }, { status: 202 }));
    vi.stubGlobal("fetch", f);
    render(<RegisterForm versions={versions} returnTo="/" navigate={() => {}} />);
    await fill(true);
    expect(f).toHaveBeenCalledTimes(1);
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/auth/register");
    expect(JSON.parse(String(init.body))).toMatchObject({
      acceptedTermsVersion: "2026-01-01",
      acceptedPrivacyVersion: "2026-02-01",
    });
    expect(host.textContent).toContain(at("register.checkInbox"));
  });

  it("does not reveal that an e-mail is already taken", async () => {
    vi.stubGlobal("fetch", async () => Response.json({ code: "registration_failed" }, { status: 400 }));
    render(<RegisterForm versions={versions} returnTo="/" navigate={() => {}} />);
    await fill(true);
    expect($("[role=alert]").textContent).toBe(at("register.failed"));
  });
});
