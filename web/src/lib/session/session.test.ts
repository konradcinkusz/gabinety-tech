import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { POST as login } from "../../app/api/auth/login/route";
import { POST as register } from "../../app/api/auth/register/route";
import { POST as logout } from "../../app/api/auth/logout/route";
import { POST as refresh, GET as refreshAndGo } from "../../app/api/auth/refresh/route";
import { GET as sessionGet } from "../../app/api/auth/session/route";
import { GET as githubStart } from "../../app/api/auth/github/start/route";
import { GET as githubCallback } from "../../app/oauth/callback/route";
import { resetBackendFetch } from "../api/http";
import { fakeBackend, makeKey, signToken, tokenBody, cookieHeader } from "../api/testUtils";

const SITE = "http://localhost:3000";
const json = (url: string, body: unknown, headers: Record<string, string> = {}) =>
  new Request(SITE + url, {
    method: "POST",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
const setCookies = (r: Response) => r.headers.getSetCookie();

beforeEach(() => {
  vi.stubEnv("Backend__Auth", "http://auth.internal");
  vi.stubEnv("Site__BaseUrl", SITE);
});
afterEach(() => {
  vi.unstubAllEnvs();
  resetBackendFetch();
});

describe("login", () => {
  it("sets HttpOnly SameSite=Lax host-only cookies and returns no token in the body", async () => {
    fakeBackend(() => tokenBody("AT-SECRET", "RT-SECRET"));
    const res = await login(json("/api/auth/login", { email: "demo@example.test", password: "pw-demo-1234" }));
    expect(res.status).toBe(200);
    const cookies = setCookies(res);
    const at = cookies.find((c) => c.startsWith("gt_at="))!;
    const rt = cookies.find((c) => c.startsWith("gt_rt="))!;
    for (const c of [at, rt]) {
      expect(c).toContain("HttpOnly");
      expect(c).toContain("SameSite=Lax");
      expect(c).toContain("Path=/");
      expect(c).not.toMatch(/Domain=/i);
      expect(c).not.toContain("Secure"); // http site
    }
    expect(at).toContain("Max-Age=3600");
    expect(rt).toContain(`Max-Age=${7 * 24 * 3600}`);
    const body = await res.text();
    expect(body).not.toContain("AT-SECRET");
    expect(body).not.toContain("RT-SECRET");
  });
  it("marks cookies Secure when the site is https", async () => {
    vi.stubEnv("Site__BaseUrl", "https://gabinety.example.test");
    fakeBackend(() => tokenBody());
    const req = new Request("https://gabinety.example.test/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ email: "a@example.test", password: "x" }),
    });
    expect(setCookies(await login(req))[0]).toContain("Secure");
  });
  it("answers a failed login 401 with a uniform code", async () => {
    fakeBackend(() => new Response("{}", { status: 401 }));
    const res = await login(json("/api/auth/login", { email: "nobody@example.test", password: "x" }));
    expect(res.status).toBe(401);
    expect(await res.json()).toMatchObject({ code: "invalid_credentials" });
    expect(setCookies(res)).toEqual([]);
  });
  it("maps authservice's 429 (its per-address auth limit) to 429 rate_limited with Retry-After, not to bad credentials", async () => {
    fakeBackend(() => Response.json({ error: "Too many requests. Please try again later.", retryAfter: 42 }, { status: 429 }));
    const res = await login(json("/api/auth/login", { email: "a@example.test", password: "x" }));
    expect(res.status).toBe(429);
    expect(await res.json()).toMatchObject({ code: "rate_limited" });
    expect(res.headers.get("retry-after")).toBe("60");
    expect(setCookies(res)).toEqual([]);
    fakeBackend(() => new Response("{}", { status: 429 }));
    expect((await register(json("/api/auth/register", { email: "a@example.test", password: "x", acceptedTermsVersion: "t", acceptedPrivacyVersion: "p" }))).status).toBe(429);
  });
  it("forwards the visitor's address to authservice in Fly-Client-IP, only a valid one, and never the visitor's own header", async () => {
    const calls = fakeBackend(() => tokenBody());
    await login(json("/api/auth/login", { email: "a@example.test", password: "x" }, { "fly-client-ip": "203.0.113.7", "x-forwarded-for": "6.6.6.6, 203.0.113.9" }));
    expect(calls[0]!.headers.get("fly-client-ip")).toBe("203.0.113.7");
    calls.length = 0;
    await login(json("/api/auth/login", { email: "a@example.test", password: "x" }, { "x-forwarded-for": "6.6.6.6, 203.0.113.9" }));
    expect(calls[0]!.headers.get("fly-client-ip")).toBe("203.0.113.9"); // the proxy's entry, not the first one the visitor wrote
    calls.length = 0;
    await login(json("/api/auth/login", { email: "a@example.test", password: "x" }, { "fly-client-ip": "not an address <script>" }));
    expect(calls[0]!.headers.has("fly-client-ip")).toBe(false);
  });
  it("maps a 2FA challenge to two_factor_not_supported without cookies", async () => {
    fakeBackend(() => Response.json({ requiresTwoFactor: true, challengeToken: "c" }));
    const res = await login(json("/api/auth/login", { email: "a@example.test", password: "x" }));
    expect(await res.json()).toMatchObject({ code: "two_factor_not_supported" });
    expect(setCookies(res)).toEqual([]);
  });
  it("maps 403 emailVerificationRequired", async () => {
    fakeBackend(() => Response.json({ emailVerificationRequired: true }, { status: 403 }));
    const res = await login(json("/api/auth/login", { email: "a@example.test", password: "x" }));
    expect(res.status).toBe(403);
    expect(await res.json()).toMatchObject({ code: "email_verification_required" });
  });
  it("rejects a cross-site login POST", async () => {
    fakeBackend(() => tokenBody());
    const res = await login(json("/api/auth/login", {}, { Origin: "https://evil.example.test" }));
    expect(res.status).toBe(403);
  });
  it("answers 503 when the identity service is down", async () => {
    fakeBackend(() => {
      throw new TypeError("fetch failed");
    });
    const res = await login(json("/api/auth/login", { email: "a@example.test", password: "x" }));
    expect(res.status).toBe(503);
    expect(await res.text()).not.toContain("auth.internal");
  });
});

describe("register", () => {
  it("sends the current consent versions when the request has none", async () => {
    const calls = fakeBackend((c) =>
      c.url.endsWith("/consents/versions")
        ? Response.json({ terms: "T9", privacy: "P9", cookies: "C9" })
        : tokenBody(),
    );
    const res = await register(json("/api/auth/register", { email: "a@example.test", password: "pw-demo-1234" }));
    expect(res.status).toBe(200);
    const reg = calls.find((c) => c.url.endsWith("/auth/register"))!;
    expect(JSON.parse(String(reg.init.body))).toMatchObject({ acceptedTermsVersion: "T9", acceptedPrivacyVersion: "P9" });
    expect(setCookies(res).length).toBe(2);
  });
  it("treats 202 as check-your-mailbox, no cookies", async () => {
    fakeBackend(() => Response.json({ emailVerificationRequired: true }, { status: 202 }));
    const res = await register(
      json("/api/auth/register", { email: "a@example.test", password: "x", acceptedTermsVersion: "1", acceptedPrivacyVersion: "1" }),
    );
    expect(res.status).toBe(202);
    expect(await res.json()).toMatchObject({ code: "verification_required" });
    expect(setCookies(res)).toEqual([]);
  });
});

describe("logout", () => {
  it("revokes upstream with the bearer and clears both cookies", async () => {
    const calls = fakeBackend(() => Response.json({ message: "ok" }));
    const res = await logout(json("/api/auth/logout", {}, { cookie: cookieHeader({ gt_at: "AT", gt_rt: "RT" }) }));
    expect(calls[0].headers.get("authorization")).toBe("Bearer AT");
    const cookies = setCookies(res);
    expect(cookies.find((c) => c.startsWith("gt_at="))).toMatch(/Max-Age=0/);
    expect(cookies.find((c) => c.startsWith("gt_rt="))).toMatch(/Max-Age=0/);
  });
});

describe("refresh", () => {
  it("dedupes five concurrent refreshes with the same refresh token", async () => {
    const calls = fakeBackend(async () => {
      await new Promise((r) => setTimeout(r, 20));
      return tokenBody("AT-NEW", "RT-NEW");
    });
    const mk = () => refresh(json("/api/auth/refresh", {}, { cookie: cookieHeader({ gt_rt: "RT-OLD" }) }));
    const res = await Promise.all([mk(), mk(), mk(), mk(), mk()]);
    expect(calls.filter((c) => c.url.endsWith("/auth/refresh")).length).toBe(1);
    for (const r of res) {
      expect(r.status).toBe(200);
      expect(setCookies(r).find((c) => c.startsWith("gt_rt="))).toContain("RT-NEW");
    }
  });
  it("clears the cookies when the identity service rejects the token", async () => {
    fakeBackend(() => new Response("{}", { status: 401 }));
    const res = await refresh(json("/api/auth/refresh", {}, { cookie: cookieHeader({ gt_rt: "RT-USED" }) }));
    expect(res.status).toBe(401);
    expect(setCookies(res).every((c) => c.includes("Max-Age=0"))).toBe(true);
  });
  it("is 401 without a refresh cookie", async () => {
    fakeBackend(() => tokenBody());
    expect((await refresh(json("/api/auth/refresh", {}))).status).toBe(401);
  });
});

describe("refresh by navigation (GET /api/auth/refresh?redirect=)", () => {
  const nav = (query: string, cookies: Record<string, string>) =>
    refreshAndGo(new Request(SITE + "/api/auth/refresh" + query, { headers: { cookie: cookieHeader(cookies) } }));
  it("renews the cookies and redirects to the safe path the user asked for", async () => {
    fakeBackend(() => tokenBody("AT-NEW", "RT-NEW"));
    const res = await nav("?redirect=%2Fmoje%2Frezerwacje%3Fa%3D1", { gt_rt: "RT-OLD" });
    expect(res.status).toBe(307);
    expect(new URL(res.headers.get("location")!, SITE).pathname + new URL(res.headers.get("location")!, SITE).search).toBe("/moje/rezerwacje?a=1");
    expect(setCookies(res).find((c) => c.startsWith("gt_at="))).toContain("AT-NEW");
    expect(setCookies(res).find((c) => c.startsWith("gt_rt="))).toContain("RT-NEW");
  });
  it("sends the user to login, with cookies cleared, when the refresh token is rejected", async () => {
    fakeBackend(() => new Response("{}", { status: 401 }));
    const res = await nav("?redirect=%2Fmoje", { gt_rt: "RT-USED" });
    expect(res.status).toBe(307);
    expect(res.headers.get("location")).toContain("/logowanie?redirect=%2Fmoje");
    expect(setCookies(res).every((c) => c.includes("Max-Age=0"))).toBe(true);
  });
  it("goes to login when there is no refresh cookie", async () => {
    fakeBackend(() => tokenBody());
    const res = await nav("?redirect=%2Fmoje", {});
    expect(res.headers.get("location")).toContain("/logowanie?redirect=%2Fmoje");
  });
  it.each(["https://evil.test/", "//evil.test", "/api/auth/refresh?redirect=%2Fx", "/api/proxy/directory/x", "javascript:alert(1)"])(
    "ignores an unsafe target %s and goes to /",
    async (target) => {
      fakeBackend(() => tokenBody("AT-NEW", "RT-NEW"));
      const res = await nav("?redirect=" + encodeURIComponent(target), { gt_rt: "RT-OLD" });
      expect(new URL(res.headers.get("location")!, SITE).pathname).toBe("/");
    },
  );
});

describe("session", () => {
  it("decodes the cookie without exposing the token", async () => {
    const key = await makeKey();
    const at = await signToken(key, { "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Admin" });
    const res = await sessionGet(new Request(SITE + "/api/auth/session", { headers: { cookie: `gt_at=${at}` } }));
    const text = await res.text();
    expect(JSON.parse(text)).toEqual({ authenticated: true, userId: "demo-user-1", email: "demo-user@example.test", isAdmin: true });
    expect(text).not.toContain(at);
  });
  it("is unauthenticated without a cookie or with garbage", async () => {
    const a = await sessionGet(new Request(SITE + "/api/auth/session"));
    expect((await a.json()).authenticated).toBe(false);
    const b = await sessionGet(new Request(SITE + "/api/auth/session", { headers: { cookie: "gt_at=garbage" } }));
    expect((await b.json()).authenticated).toBe(false);
  });
});

const STATE_COOKIE = "gt_oauth_state";
const stateOf = (r: Response) => {
  const c = setCookies(r).find((x) => x.startsWith(STATE_COOKIE + "="))!;
  return { header: c, value: c.slice(STATE_COOKIE.length + 1).split(";")[0] };
};
const callbackReq = (qs: string, cookie?: string) =>
  new Request(`${SITE}/oauth/callback?${qs}`, cookie ? { headers: { cookie } } : undefined);

describe("github", () => {
  it("start redirects to the identity service with returnUrl built from Site__BaseUrl", async () => {
    const res = await githubStart(new Request("http://evil.example.test/api/auth/github/start", { headers: { host: "evil.example.test" } }));
    expect(res.status).toBe(302);
    const loc = new URL(res.headers.get("location")!);
    expect(loc.origin).toBe("http://auth.internal");
    expect(loc.pathname).toBe("/api/v1/external-auth/login");
    expect(loc.searchParams.get("provider")).toBe("GitHub");
    expect(loc.searchParams.get("returnUrl")).toMatch(/^http:\/\/localhost:3000\/oauth\/callback/);
  });
  it("start's returnUrl satisfies authservice's IsAllowedReturnUrl (v0.3.4 ExternalAuthController.cs:344-352)", async () => {
    // The rule: absolute URL, same scheme+host+port as an allowed base (here Site__BaseUrl), path /oauth/callback or below it.
    const isAllowed = (returnUrl: string, bases: string[]) => {
      let c: URL;
      try {
        c = new URL(returnUrl);
      } catch {
        return false;
      }
      const p = c.pathname.toLowerCase();
      if (!(p === "/oauth/callback" || p.startsWith("/oauth/callback/"))) return false;
      return bases.some((b) => {
        const a = new URL(b);
        return c.protocol === a.protocol && c.hostname === a.hostname && c.port === a.port;
      });
    };
    expect(isAllowed(SITE + "/api/auth/github/callback", [SITE])).toBe(false); // the old value: authservice answered 400
    for (const q of ["", "?redirect=" + encodeURIComponent("/moje?x=1")]) {
      const res = await githubStart(new Request(SITE + "/api/auth/github/start" + q));
      const ret = new URL(res.headers.get("location")!).searchParams.get("returnUrl")!;
      expect(isAllowed(ret, [SITE])).toBe(true);
      expect(new URL(ret).searchParams.get("state")).toBeTruthy();
    }
  });
  it("start sets a random HttpOnly SameSite=Lax short-lived state cookie and puts the same state in returnUrl (F-05)", async () => {
    const a = await githubStart(new Request(SITE + "/api/auth/github/start"));
    const b = await githubStart(new Request(SITE + "/api/auth/github/start"));
    const { header, value } = stateOf(a);
    expect(value.length).toBeGreaterThanOrEqual(32);
    expect(value).not.toBe(stateOf(b).value);
    expect(header).toContain("HttpOnly");
    expect(header).toContain("SameSite=Lax");
    expect(header).toMatch(/Max-Age=(\d+)/);
    expect(Number(/Max-Age=(\d+)/.exec(header)![1])).toBeLessThanOrEqual(600);
    const ret = new URL(new URL(a.headers.get("location")!).searchParams.get("returnUrl")!);
    expect(ret.searchParams.get("state")).toBe(value);
  });
  it("callback exchanges the code, sets cookies and redirects to a safe relative path", async () => {
    const calls = fakeBackend(() => tokenBody());
    const res = await githubCallback(callbackReq("code=abc&state=s-1&redirect=/moje", `${STATE_COOKIE}=s-1`));
    expect(JSON.parse(String(calls[0].init.body))).toEqual({ code: "abc" });
    expect(res.headers.get("location")).toBe(SITE + "/moje");
    expect(setCookies(res).filter((c) => c.startsWith("gt_")).length).toBe(3); // at, rt, state cleared
  });
  it("callback deletes the state cookie after use (one shot)", async () => {
    fakeBackend(() => tokenBody());
    const res = await githubCallback(callbackReq("code=abc&state=s-1", `${STATE_COOKIE}=s-1`));
    expect(stateOf(res).header).toMatch(/Max-Age=0/);
  });
  it.each([
    ["no state cookie (login CSRF: the victim never started)", "code=abc&state=s-1", undefined],
    ["no state in the URL", "code=abc", `${STATE_COOKIE}=s-1`],
    ["a different state", "code=abc&state=s-2", `${STATE_COOKIE}=s-1`],
    ["an empty state on both sides", "code=abc&state=", `${STATE_COOKIE}=`],
    ["a state of another length", "code=abc&state=s-11", `${STATE_COOKIE}=s-1`],
  ])("callback refuses %s: no exchange, no session", async (_n, qs, cookie) => {
    const calls = fakeBackend(() => tokenBody());
    const res = await githubCallback(callbackReq(qs, cookie));
    expect(calls.length).toBe(0);
    expect(res.headers.get("location")).toContain("/logowanie?error=github_failed");
    expect(setCookies(res).some((c) => c.startsWith("gt_at=") || c.startsWith("gt_rt="))).toBe(false);
  });
  it.each(["https://evil.example.test/x", "//evil.example.test", "/\\evil.example.test", "javascript:alert(1)"])(
    "callback ignores the redirect target %s",
    async (target) => {
      fakeBackend(() => tokenBody());
      const res = await githubCallback(callbackReq(`code=abc&state=s-1&redirect=${encodeURIComponent(target)}`, `${STATE_COOKIE}=s-1`));
      expect(res.headers.get("location")).toBe(SITE + "/");
    },
  );
  it("start keeps a safe redirect and drops an unsafe one", async () => {
    const ok = await githubStart(new Request(SITE + "/api/auth/github/start?redirect=" + encodeURIComponent("/moje?x=1")));
    expect(new URL(new URL(ok.headers.get("location")!).searchParams.get("returnUrl")!).searchParams.get("redirect")).toBe("/moje?x=1");
    const bad = await githubStart(new Request(SITE + "/api/auth/github/start?redirect=" + encodeURIComponent("//evil.example.test")));
    expect(new URL(new URL(bad.headers.get("location")!).searchParams.get("returnUrl")!).searchParams.has("redirect")).toBe(false);
  });
  it("callback without a code goes back to login with an error code", async () => {
    const res = await githubCallback(callbackReq("state=s-1", `${STATE_COOKIE}=s-1`));
    expect(res.headers.get("location")).toContain("/logowanie?error=");
    expect(setCookies(res).some((c) => c.startsWith("gt_at="))).toBe(false);
  });
});
