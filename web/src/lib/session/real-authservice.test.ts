// The web's own auth handlers (BFF) and account calls against the REAL authservice, not a fake: it runs only when AUTH_BASE_URL names one, as the CI `integration` job does
// (image pinned by digest, PostgreSQL, RS256). Everything else in this folder runs the same handlers against a fake. A mismatch between the two is what this file exists to find.
// AUTH_ADMIN_EMAIL / AUTH_ADMIN_PASSWORD (the InitialAdmin of that instance) enable the administrator check; AUTH_GITHUB_CONFIGURED=1 the GitHub start check (dummy OAuth credentials).
import { createHmac } from "node:crypto";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { POST as login } from "../../app/api/auth/login/route";
import { POST as register } from "../../app/api/auth/register/route";
import { POST as logout } from "../../app/api/auth/logout/route";
import { POST as refresh } from "../../app/api/auth/refresh/route";
import { GET as sessionGet } from "../../app/api/auth/session/route";
import { GET as githubStart } from "../../app/api/auth/github/start/route";
import { GET as githubCallback } from "../../app/oauth/callback/route";
import { deleteAccount, exportAccount, loadAccount } from "../../components/auth/account.server";
import { resetBackendFetch } from "../api/http";

const BASE = process.env.AUTH_BASE_URL;
const SITE = "http://localhost:3000";
const PASSWORD = "Web-Real-Pass-123!";

let visitor = "10.0.0.1";
const post = (url: string, body: unknown, cookie?: string, from?: string) =>
  new Request(SITE + url, {
    method: "POST",
    headers: { "content-type": "application/json", origin: SITE, "fly-client-ip": from ?? visitor, ...(cookie ? { cookie } : {}) },
    body: JSON.stringify(body),
  });
const cookiesOf = (r: Response) =>
  Object.fromEntries(
    r.headers.getSetCookie().map((c) => {
      const pair = c.split(";")[0]!;
      const eq = pair.indexOf("="); // a base64 value may itself contain "="
      return [pair.slice(0, eq), pair.slice(eq + 1)] as [string, string];
    }),
  );
const header = (c: Record<string, string>) => Object.entries(c).map(([k, v]) => `${k}=${v}`).join("; ");
const email = () => `demo-web-${crypto.randomUUID().replaceAll("-", "")}@example.test`;

async function signUp(address = email()) {
  const res = await register(post("/api/auth/register", { email: address, password: PASSWORD }));
  expect(res.status).toBe(200);
  return { address, cookies: cookiesOf(res) };
}

describe.skipIf(!BASE)("the BFF auth handlers against the real authservice", () => {
  beforeEach(() => {
    vi.stubEnv("Backend__Auth", BASE!);
    vi.stubEnv("Site__BaseUrl", SITE);
    // The instance runs with Network__ClientIpHeader=Fly-Client-IP, as deploy/fly/authservice.fly.toml: the handlers forward the visitor's address in it, so each test
    // is its own visitor (a distinct fly-client-ip on the request) and the file does not queue behind one bucket.
    visitor = `10.${Math.floor(Math.random() * 250)}.${Math.floor(Math.random() * 250)}.${Math.floor(Math.random() * 250) + 1}`;
  });
  afterEach(() => {
    vi.unstubAllEnvs();
    resetBackendFetch();
  });

  it("registers (the web fetches the consent versions itself), logs in, reads the session and logs out", async () => {
    const { address, cookies } = await signUp();
    expect(cookies.gt_at).toBeTruthy();
    expect(cookies.gt_rt).toBeTruthy();

    const who = await (await sessionGet(new Request(SITE + "/api/auth/session", { headers: { cookie: header({ gt_at: cookies.gt_at! }) } }))).json();
    expect(who).toMatchObject({ authenticated: true, email: address, isAdmin: false });

    const wrong = await login(post("/api/auth/login", { email: address, password: PASSWORD + "-wrong" }));
    expect(wrong.status).toBe(401);
    expect(await wrong.json()).toMatchObject({ code: "invalid_credentials" });

    const ok = await login(post("/api/auth/login", { email: address, password: PASSWORD }));
    expect(ok.status).toBe(200);
    const signedIn = cookiesOf(ok);

    const out = await logout(post("/api/auth/logout", {}, header({ gt_at: signedIn.gt_at!, gt_rt: signedIn.gt_rt! })));
    expect(out.status).toBe(200);
    // authservice revoked every refresh token of the user: the cookie that was just logged out can no longer renew anything.
    const after = await refresh(post("/api/auth/refresh", {}, header({ gt_rt: signedIn.gt_rt! })));
    expect(after.status).toBe(401);
  });

  it("a registration with a weak password or an address already taken is the one uniform 400", async () => {
    const taken = (await signUp()).address;
    const weak = await register(post("/api/auth/register", { email: email(), password: "password" }));
    const twice = await register(post("/api/auth/register", { email: taken, password: PASSWORD }));
    for (const res of [weak, twice]) {
      expect(res.status).toBe(400);
      expect(await res.json()).toMatchObject({ code: "registration_failed" });
    }
  });

  it("rotates the refresh cookie, and a replayed one ends the session (cookies cleared)", async () => {
    const { cookies } = await signUp();
    const first = await refresh(post("/api/auth/refresh", {}, header({ gt_rt: cookies.gt_rt! })));
    expect(first.status).toBe(200);
    const rotated = cookiesOf(first);
    expect(rotated.gt_rt).not.toBe(cookies.gt_rt);

    const replay = await refresh(post("/api/auth/refresh", {}, header({ gt_rt: cookies.gt_rt! })));
    expect(replay.status).toBe(401);
    expect(replay.headers.getSetCookie().join(";")).toMatch(/gt_rt=;|gt_rt=.*Max-Age=0/i);
    // the whole family is gone, including the token the first refresh issued
    expect((await refresh(post("/api/auth/refresh", {}, header({ gt_rt: rotated.gt_rt! })))).status).toBe(401);
  });

  it("the account page reads what authservice really answers, the export is a download, a wrong password does not delete, the right one does", async () => {
    const { address, cookies } = await signUp();
    const token = cookies.gt_at!;

    const account = await loadAccount(token);
    expect(account).toMatchObject({ email: address, emailConfirmed: true, hasPassword: true, terms: { accepted: true }, privacy: { accepted: true } });
    expect(account!.terms.version).toMatch(/\S/);

    const exported = await exportAccount(token);
    expect(exported.status).toBe(200);
    expect(exported.headers.get("content-disposition")).toContain("attachment");
    expect(await exported.json()).toMatchObject({ profile: { email: address } });

    expect(await deleteAccount(token, PASSWORD + "-wrong")).toBe("rejected");
    expect((await login(post("/api/auth/login", { email: address, password: PASSWORD }))).status).toBe(200);
    expect(await deleteAccount(token, PASSWORD)).toBe("ok");
    expect((await login(post("/api/auth/login", { email: address, password: PASSWORD }))).status).toBe(401);
  });

  it("an account with two-factor authentication gets two_factor_not_supported and no cookies", async () => {
    const { address, cookies } = await signUp();
    const bearer = { authorization: `Bearer ${cookies.gt_at}`, "content-type": "application/json" };
    const setup = (await (await fetch(`${BASE}/api/v1/auth/2fa/enable`, { method: "POST", headers: bearer })).json()) as { sharedKey: string };
    const code = totp(setup.sharedKey);
    expect((await fetch(`${BASE}/api/v1/auth/2fa/verify`, { method: "POST", headers: bearer, body: JSON.stringify({ code }) })).status).toBe(200);

    const res = await login(post("/api/auth/login", { email: address, password: PASSWORD }));

    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ code: "two_factor_not_supported" });
    expect(res.headers.getSetCookie()).toEqual([]);
  });

  it.skipIf(!process.env.AUTH_ADMIN_EMAIL)("the seeded administrator signs in and the session says isAdmin", async () => {
    const res = await login(post("/api/auth/login", { email: process.env.AUTH_ADMIN_EMAIL, password: process.env.AUTH_ADMIN_PASSWORD }));
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ authenticated: true, isAdmin: true });
  });

  it("two visitors do not share a bucket: one exhausting the 20 a minute gets 429, another is still served", async () => {
    // 30 logins at once from one address: 20 pass (as 401), 5 queue and the rest get 429 at once (authservice Program.cs). The queued ones outlast the handler's timeout (503).
    const burst = await Promise.all(
      Array.from({ length: 30 }, () => login(post("/api/auth/login", { email: email(), password: PASSWORD }, undefined, "10.200.200.1"))),
    );
    const codes = burst.map((r) => r.status);
    expect(codes).toContain(429);
    expect(codes).toContain(401);

    const other = await login(post("/api/auth/login", { email: email(), password: PASSWORD }, undefined, "10.200.200.2"));
    expect(other.status).toBe(401); // a wrong password, not "slow down"
    const same = await login(post("/api/auth/login", { email: email(), password: PASSWORD }, undefined, "10.200.200.1"));
    expect([429, 503]).toContain(same.status); // the first visitor is still limited
  }, 40_000);

  it("an exchange code authservice never issued ends in the login page with github_failed", async () => {
    const start = await githubStart(new Request(SITE + "/api/auth/github/start"));
    const state = new URL(start.headers.get("location")!).searchParams.get("returnUrl")!.match(/state=([^&]+)/)![1]!;
    const stateCookie = cookiesOf(start);
    const res = await githubCallback(new Request(`${SITE}/oauth/callback?code=never-issued&state=${state}`, { headers: { cookie: header(stateCookie) } }));
    expect(res.status).toBe(302);
    expect(res.headers.get("location")).toContain("/logowanie?error=github_failed");
  });

  it.skipIf(process.env.AUTH_GITHUB_CONFIGURED !== "1")("the URL the web builds for GitHub sign-in is accepted by authservice and answered with a redirect to github.com", async () => {
    const start = await githubStart(new Request(SITE + "/api/auth/github/start?redirect=%2Fmoje%2Frezerwacje"));
    expect(start.status).toBe(302);

    const res = await fetch(start.headers.get("location")!, { redirect: "manual" });

    expect(res.status).toBe(302);
    const target = new URL(res.headers.get("location")!);
    expect(target.origin + target.pathname).toBe("https://github.com/login/oauth/authorize");
    expect(target.searchParams.get("state")).toBeTruthy();
  });
});

/** RFC 6238 (SHA-1, 30 s, 6 digits) from the base32 key authservice hands out. */
function totp(sharedKey: string): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits = 0;
  let value = 0;
  const bytes: number[] = [];
  for (const ch of sharedKey.replaceAll(/[\s=-]/g, "").toUpperCase()) {
    value = (value << 5) | alphabet.indexOf(ch);
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 1000 / 30)));
  const h = createHmac("sha1", Buffer.from(bytes)).update(counter).digest();
  const o = h[h.length - 1]! & 0xf;
  const n = ((h[o]! & 0x7f) << 24) | (h[o + 1]! << 16) | (h[o + 2]! << 8) | h[o + 3]!;
  return String(n % 1_000_000).padStart(6, "0");
}
