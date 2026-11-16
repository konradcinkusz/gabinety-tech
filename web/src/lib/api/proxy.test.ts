import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { DELETE, GET, POST } from "../../app/api/proxy/[...path]/route";
import { resetBackendFetch } from "./http";
import { resetBackendMemory } from "./backends";
import { timeoutFor } from "./proxy";
import { cookieHeader, fakeBackend, tokenBody } from "./testUtils";

const SITE = "http://localhost:3000";
// The matched segments are the raw split of the path: new URL() would collapse `..` in request.url.
const ctx = (path: string) => ({ params: Promise.resolve({ path: path.split("/") }) });
const get = (path: string, headers: Record<string, string> = {}) =>
  GET(new Request(SITE + "/api/proxy/" + path, { headers }), ctx(path));
const post = (path: string, headers: Record<string, string> = {}, body = "{}") =>
  POST(new Request(SITE + "/api/proxy/" + path, { method: "POST", headers, body }), ctx(path));

beforeEach(() => {
  vi.stubEnv("Backend__Directory", "http://directory.internal:5101");
  vi.stubEnv("Backend__Auth", "http://auth.internal:5105");
  vi.stubEnv("Site__BaseUrl", SITE);
});
afterEach(() => {
  vi.unstubAllEnvs();
  resetBackendFetch();
  resetBackendMemory();
});

describe("proxy forwarding", () => {
  it("forwards to the selected backend with path and query", async () => {
    const calls = fakeBackend(() => Response.json([]));
    await get("directory/api/v1/practitioners?city=Krakow");
    expect(calls[0].url).toBe("http://directory.internal:5101/api/v1/practitioners?city=Krakow");
  });
  it("never forwards the Cookie header and forwards the bearer", async () => {
    const calls = fakeBackend(() => Response.json({}));
    await get("directory/api/v1/me", { cookie: cookieHeader({ gt_at: "AT-1", gt_rt: "RT-1", other: "x" }), Accept: "application/json" });
    expect(calls[0].headers.get("cookie")).toBeNull();
    expect(calls[0].headers.get("authorization")).toBe("Bearer AT-1");
    expect(calls[0].headers.get("accept")).toBe("application/json");
  });
  it("strips headers outside the allow-list, forwards the allowed ones", async () => {
    const calls = fakeBackend(() => Response.json({}));
    await post("directory/api/v1/x", {
      "Idempotency-Key": "k1",
      "Content-Type": "application/json",
      "Accept-Language": "pl",
      "If-None-Match": '"e"',
      "X-Forwarded-For": "1.2.3.4",
      Authorization: "Bearer attacker-supplied",
      "X-Custom": "1",
    });
    const h = calls[0].headers;
    expect(h.get("idempotency-key")).toBe("k1");
    expect(h.get("content-type")).toBe("application/json");
    expect(h.get("accept-language")).toBe("pl");
    expect(h.get("if-none-match")).toBe('"e"');
    expect(h.get("x-forwarded-for")).toBeNull();
    expect(h.get("x-custom")).toBeNull();
    expect(h.get("authorization")).toBeNull(); // no gt_at cookie: the client's own bearer is never trusted through
  });
  it("drops Set-Cookie from the backend and streams the body", async () => {
    fakeBackend(() => new Response("hello", { status: 200, headers: { "Set-Cookie": "evil=1", "Content-Type": "text/plain" } }));
    const res = await get("directory/api/v1/x");
    expect(res.headers.get("set-cookie")).toBeNull();
    expect(await res.text()).toBe("hello");
  });
});

describe("proxy path safety", () => {
  it.each([
    "directory/../auth/api/v1/x",
    "directory/%2e%2e/auth",
    "directory/%2E%2E/auth",
    "directory/a%2f..%2fb",
    "directory/a%5c..%5cb",
    "directory//evil.example.test/x",
    "directory/%252e%252e/x",
    "directory/%zz",
  ])("rejects %s with 400", async (p) => {
    const calls = fakeBackend(() => Response.json({}));
    expect((await get(p)).status).toBe(400);
    expect(calls).toHaveLength(0);
  });
  it("answers an unknown backend segment with 404", async () => {
    const calls = fakeBackend(() => Response.json({}));
    expect((await get("auth/api/v1/x")).status).toBe(404);
    expect((await get("billing/api/v1/x")).status).toBe(404);
    expect(calls).toHaveLength(0);
  });
});

describe("proxy guards and limits", () => {
  it("rejects a cross-site POST with 403", async () => {
    const calls = fakeBackend(() => Response.json({}));
    const res = await post("directory/api/v1/x", { Origin: "https://evil.example.test" });
    expect(res.status).toBe(403);
    expect(calls).toHaveLength(0);
  });
  it("passes a same-origin POST", async () => {
    fakeBackend(() => Response.json({ ok: 1 }));
    expect((await post("directory/api/v1/x", { Origin: SITE })).status).toBe(200);
  });
  it("rejects a body over 1 MB with 413", async () => {
    const calls = fakeBackend(() => Response.json({}));
    const res = await post("directory/api/v1/x", {}, "x".repeat(1024 * 1024 + 1));
    expect(res.status).toBe(413);
    expect(calls).toHaveLength(0);
  });
  it("answers 503 problem+json without the backend URL when the backend is down", async () => {
    fakeBackend(() => {
      throw new TypeError("connect ECONNREFUSED directory.internal:5101");
    });
    const res = await get("directory/api/v1/x");
    expect(res.status).toBe(503);
    expect(res.headers.get("content-type")).toContain("application/problem+json");
    const text = await res.text();
    expect(JSON.parse(text).type).toBe("urn:gabinety:error:dependency_unavailable");
    expect(text).not.toContain("directory.internal");
    expect(text).not.toContain("5101");
  });
  it("answers 503 when the backend is not configured", async () => {
    vi.stubEnv("Backend__Booking", "");
    expect((await get("booking/api/v1/x")).status).toBe(503);
  });
  it("follows the SPEC 5.1 timeout table", () => {
    expect(timeoutFor("GET", "/api/v1/practitioners")).toBe(5000);
    expect(timeoutFor("HEAD", "/api/v1/practitioners")).toBe(5000);
    expect(timeoutFor("POST", "/api/v1/bookings")).toBe(15000);
    expect(timeoutFor("POST", "/api/v1/assistant/brief")).toBe(35000);
    expect(timeoutFor("POST", "/api/v1/other")).toBe(10000);
    expect(timeoutFor("DELETE", "/api/v1/other")).toBe(10000);
    expect(timeoutFor("GET", "/api/v1/bookings")).toBe(5000);
  });
});

describe("proxy refresh on 401", () => {
  const cookies = { cookie: cookieHeader({ gt_at: "AT-OLD", gt_rt: "RT-OLD" }) };
  const handler = (c: { url: string; headers: Headers }) => {
    if (c.url.endsWith("/auth/refresh")) return tokenBody("AT-NEW", "RT-NEW");
    return c.headers.get("authorization") === "Bearer AT-NEW"
      ? Response.json({ ok: true })
      : new Response("{}", { status: 401 });
  };
  it("refreshes once and retries once for GET, and sets the new cookies", async () => {
    const calls = fakeBackend(handler);
    const res = await get("directory/api/v1/me", cookies);
    expect(res.status).toBe(200);
    expect(calls.filter((c) => c.url.endsWith("/auth/refresh"))).toHaveLength(1);
    expect(calls.filter((c) => c.url.includes("/api/v1/me"))).toHaveLength(2);
    expect(res.headers.getSetCookie().find((c) => c.startsWith("gt_at="))).toContain("AT-NEW");
  });
  it("does not retry a POST", async () => {
    const calls = fakeBackend(handler);
    const res = await post("directory/api/v1/me", cookies);
    expect(res.status).toBe(401);
    expect(calls.filter((c) => c.url.includes("/api/v1/me"))).toHaveLength(1);
  });
  it("does not retry DELETE", async () => {
    const calls = fakeBackend(handler);
    const res = await DELETE(new Request(SITE + "/api/proxy/directory/api/v1/me", { method: "DELETE", headers: cookies }), ctx("directory/api/v1/me"));
    expect(res.status).toBe(401);
    expect(calls.filter((c) => c.url.includes("/api/v1/me"))).toHaveLength(1);
  });
  it("clears the cookies when the refresh is rejected", async () => {
    fakeBackend(() => new Response("{}", { status: 401 }));
    const res = await get("directory/api/v1/me", cookies);
    expect(res.status).toBe(401);
    expect(res.headers.getSetCookie().every((c) => c.includes("Max-Age=0"))).toBe(true);
  });
  it("refreshes first when only the refresh cookie is left", async () => {
    const calls = fakeBackend(handler);
    const res = await get("directory/api/v1/me", { cookie: cookieHeader({ gt_rt: "RT-OLD" }) });
    expect(res.status).toBe(200);
    expect(calls[0].url).toMatch(/\/auth\/refresh$/);
  });
});

describe("backend ladder", () => {
  it("picks the first candidate that answers and remembers it for 30 s", async () => {
    vi.stubEnv("Backend__Directory", "http://down.invalid:1, http://up.invalid:2");
    const calls = fakeBackend((c) => {
      if (c.url.startsWith("http://down.invalid")) throw new TypeError("fetch failed");
      return Response.json({});
    });
    await get("directory/api/v1/a");
    await get("directory/api/v1/b");
    const probes = calls.filter((c) => c.url.startsWith("http://down.invalid"));
    expect(probes).toHaveLength(1); // probed once, then remembered
    expect(calls.filter((c) => c.url.startsWith("http://up.invalid:2/api/v1/"))).toHaveLength(2);
  });
});
