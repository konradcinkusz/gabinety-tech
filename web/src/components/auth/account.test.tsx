import { renderToStaticMarkup } from "react-dom/server";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetBackendFetch } from "../../lib/api/http";
import { fakeBackend } from "../../lib/api/testUtils";
import { AccountView } from "./AccountView";
import { deleteAccount, exportAccount, loadAccount } from "./account.server";

const TOKEN = "AT-SECRET-eyJhbGciOi";
const me = {
  id: "demo-user-1",
  email: "demo-user@example.test",
  emailConfirmed: false,
  hasPassword: true,
  // fields a careless page might dump:
  accessToken: TOKEN,
  refreshToken: "RT-SECRET",
  organizations: [{ id: "o1" }],
};
const consents = {
  terms: { requiredVersion: "2026-01-01", acceptedVersion: "2025-06-01", accepted: false },
  privacy: { requiredVersion: "2026-02-01", acceptedVersion: "2026-02-01", accepted: true },
  requiresConsent: true,
};

beforeEach(() => vi.stubEnv("Backend__Auth", "http://auth.internal"));
afterEach(() => {
  vi.unstubAllEnvs();
  resetBackendFetch();
});

describe("account page", () => {
  it("shows e-mail, verification state and consents, and no token", async () => {
    const calls = fakeBackend((c) =>
      c.url.endsWith("/auth/me") ? Response.json(me) : Response.json(consents),
    );
    const account = await loadAccount(TOKEN);
    expect(account).not.toBeNull();
    const html = renderToStaticMarkup(<AccountView account={account!} actions={{ accept: async () => {}, remove: async () => {} }} />);
    expect(html).toContain("demo-user@example.test");
    expect(html).toContain("/konto/export");
    expect(html).not.toContain(TOKEN);
    expect(html).not.toContain("RT-SECRET");
    expect(html.toLowerCase()).not.toContain("token");
    expect(JSON.stringify(account)).not.toContain("SECRET");
    expect(calls.every((c) => c.headers.get("authorization") === `Bearer ${TOKEN}`)).toBe(true);
  });

  it("returns null for a rejected token instead of leaking the error", async () => {
    fakeBackend(() => new Response("{}", { status: 401 }));
    expect(await loadAccount(TOKEN)).toBeNull();
  });

  it("exports through the BFF with the bearer and attaches the file", async () => {
    fakeBackend(() => Response.json({ exportedAt: "2026-11-17T10:00:00Z", profile: {} }));
    const res = await exportAccount(TOKEN);
    expect(res.status).toBe(200);
    expect(res.headers.get("content-disposition")).toContain("attachment");
    expect(res.headers.get("cache-control")).toBe("no-store");
  });

  it("sends the DELETE confirmation and the password only when the account has one", async () => {
    const calls = fakeBackend(() => Response.json({ message: "ok" }));
    expect(await deleteAccount(TOKEN, "pw-demo-1234")).toBe("ok");
    expect(calls[0]!.init.method).toBe("DELETE");
    expect(JSON.parse(String(calls[0]!.init.body))).toEqual({ password: "pw-demo-1234", confirmation: "DELETE" });
    fakeBackend(() => new Response("{}", { status: 400 }));
    expect(await deleteAccount(TOKEN, "bad")).toBe("rejected");
  });
});
