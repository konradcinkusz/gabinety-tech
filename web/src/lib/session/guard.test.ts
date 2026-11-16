import { createServer, type Server } from "node:http";
import type { AddressInfo } from "node:net";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { createRemoteJWKSet } from "jose";
import { decide, createVerifier, type Verifier } from "./guard";
import { makeKey, signToken } from "../api/testUtils";

const ADMIN_ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const fake: Verifier = async (t) => {
  if (t === "good") return { sub: "u" };
  if (t === "admin") return { sub: "u", [ADMIN_ROLE]: "Admin" };
  if (t === "admins") return { sub: "u", [ADMIN_ROLE]: ["User", "SuperAdmin"] };
  throw new Error("bad token");
};
const input = (pathname: string, at?: string, search = "", refresh?: string) => ({ pathname, search, accessToken: at, refreshToken: refresh });

describe("decide with a refresh cookie", () => {
  it("renews through /api/auth/refresh when the access cookie is gone but a refresh cookie remains", async () => {
    expect(await decide(input("/moje", undefined, "?a=1", "RT"), fake)).toEqual({
      action: "redirect",
      to: "/api/auth/refresh?redirect=" + encodeURIComponent("/moje?a=1"),
    });
  });
  it("does not loop: a present but invalid access token goes to login even with a refresh cookie", async () => {
    expect((await decide(input("/moje", "altered", "", "RT"), fake)).action).toBe("redirect");
    expect(await decide(input("/moje", "altered", "", "RT"), fake)).toEqual({ action: "redirect", to: "/logowanie?redirect=%2Fmoje" });
  });
  it("still sends a visitor with no cookies to login", async () => {
    expect(await decide(input("/moje"), fake)).toEqual({ action: "redirect", to: "/logowanie?redirect=%2Fmoje" });
  });
  it("leaves public pages alone", async () => {
    expect(await decide(input("/programisci", undefined, "", "RT"), fake)).toEqual({ action: "next" });
  });
});

describe("decide (fake verifier)", () => {
  it("lets a valid token through on a protected prefix", async () => {
    expect(await decide(input("/moje/rezerwacje", "good"), fake)).toEqual({ action: "next" });
  });
  it("redirects with the original path and query when there is no cookie", async () => {
    expect(await decide(input("/moje", undefined, "?a=1"), fake)).toEqual({
      action: "redirect",
      to: "/logowanie?redirect=" + encodeURIComponent("/moje?a=1"),
    });
  });
  it("redirects when the verifier rejects", async () => {
    expect((await decide(input("/rezerwacja/5", "expired"), fake)).action).toBe("redirect");
  });
  it.each(["/moje", "/gabinet-programisty", "/admin", "/rezerwacja", "/checkout", "/konto/ustawienia"])(
    "protects %s",
    async (p) => expect((await decide(input(p), fake)).action).toBe("redirect"),
  );
  it.each(["/", "/logowanie", "/rejestracja", "/szukaj", "/mojeinne", "/api/proxy/directory/x", "/_next/static/a.js", "/favicon.ico"])(
    "does not protect %s",
    async (p) => expect(await decide(input(p), fake)).toEqual({ action: "next" }),
  );
  it("sends a non-admin away from /admin to /", async () => {
    expect(await decide(input("/admin/uzytkownicy", "good"), fake)).toEqual({ action: "redirect", to: "/" });
  });
  it("lets Admin and SuperAdmin into /admin", async () => {
    expect((await decide(input("/admin", "admin"), fake)).action).toBe("next");
    expect((await decide(input("/admin", "admins"), fake)).action).toBe("next");
  });
});

describe("decide with real jose", () => {
  let server: Server;
  let base: string;
  let key: Awaited<ReturnType<typeof makeKey>>;
  let other: Awaited<ReturnType<typeof makeKey>>;
  beforeAll(async () => {
    key = await makeKey("k1");
    other = await makeKey("k1"); // same kid, different key material
    server = createServer((req, res) => {
      if (req.url === "/.well-known/jwks.json") {
        res.setHeader("content-type", "application/json");
        res.end(JSON.stringify({ keys: [key.jwk] }));
      } else res.writeHead(404).end();
    });
    await new Promise<void>((r) => server.listen(0, "127.0.0.1", r));
    base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  });
  afterAll(() => server.close());

  const verifier = (extra: { issuer?: string; audience?: string } = {}) =>
    createVerifier(createRemoteJWKSet(new URL(base + "/.well-known/jwks.json")), extra);

  it("accepts a correctly signed token", async () => {
    const t = await signToken(key);
    expect((await decide(input("/moje", t), verifier())).action).toBe("next");
  });
  it("redirects when one character of the signature is altered", async () => {
    const t = await signToken(key);
    const i = t.length - 5;
    const altered = t.slice(0, i) + (t[i] === "A" ? "B" : "A") + t.slice(i + 1);
    expect((await decide(input("/moje", altered), verifier())).action).toBe("redirect");
  });
  it("redirects when the payload is altered", async () => {
    const t = await signToken(key);
    const [h, p, s] = t.split(".");
    const forged = Buffer.from(JSON.stringify({ ...JSON.parse(Buffer.from(p, "base64url").toString()), [ADMIN_ROLE]: "Admin" })).toString("base64url");
    expect((await decide(input("/admin", `${h}.${forged}.${s}`), verifier())).action).toBe("redirect");
  });
  it("redirects a token signed by another key", async () => {
    const t = await signToken(other);
    expect((await decide(input("/moje", t), verifier())).action).toBe("redirect");
  });
  it("redirects an expired token", async () => {
    const t = await signToken(key, {}, { exp: Math.floor(Date.now() / 1000) - 3600 });
    expect((await decide(input("/moje", t), verifier())).action).toBe("redirect");
  });
  it("redirects a wrong audience or issuer when configured, accepts the right ones", async () => {
    const t = await signToken(key, {}, { aud: "SomethingElse" });
    expect((await decide(input("/moje", t), verifier({ audience: "AuthService" }))).action).toBe("redirect");
    const u = await signToken(key, {}, { iss: "Evil" });
    expect((await decide(input("/moje", u), verifier({ issuer: "AuthService" }))).action).toBe("redirect");
    const ok = await signToken(key);
    expect((await decide(input("/moje", ok), verifier({ issuer: "AuthService", audience: "AuthService" }))).action).toBe("next");
  });
  it("lets an unsigned alg=none token never pass", async () => {
    const none = `${Buffer.from('{"alg":"none"}').toString("base64url")}.${Buffer.from('{"sub":"u"}').toString("base64url")}.`;
    expect((await decide(input("/moje", none), verifier())).action).toBe("redirect");
  });
});
