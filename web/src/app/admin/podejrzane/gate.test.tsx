import { SignJWT } from "jose";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

let cookie: string | undefined;
vi.mock("next/headers", () => ({ cookies: async () => ({ get: (n: string) => (n === "gt_at" && cookie ? { value: cookie } : undefined) }) }));
const backend = vi.fn();
const resolved = vi.fn(async (_service: string) => "http://reputation.test");
vi.mock("@/lib/api/backends", () => ({ resolveBackend: (s: string) => resolved(s), forgetBackend: () => {} }));
vi.mock("@/lib/api/http", () => ({ backendFetch: (...a: unknown[]) => backend(...a) }));

const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const token = (roles: string[]) =>
  new SignJWT({ [ROLE]: roles }).setProtectedHeader({ alg: "HS256" }).setSubject("demo-u").setExpirationTime("1h").sign(new TextEncoder().encode("demo-secret-demo-secret-demo-secret!"));

beforeEach(() => { backend.mockReset(); resolved.mockClear(); });
afterEach(() => { cookie = undefined; });

describe("/admin/podejrzane is admin-only at the page, not only at the edge", async () => {
  const { default: Page } = await import("./page");
  const search = (o: Record<string, string> = {}) => ({ searchParams: Promise.resolve(o) });

  it("a signed-out visitor is sent to sign in with the way back", async () => {
    await expect(Page(search())).rejects.toMatchObject({ digest: expect.stringContaining("/logowanie?redirect=") });
    expect(backend).not.toHaveBeenCalled();
  });
  it("a signed-in non-admin gets 'forbidden' and Reputation is never asked", async () => {
    cookie = await token(["User"]);
    expect(JSON.stringify(await Page(search()))).toContain("forbidden");
    expect(backend).not.toHaveBeenCalled();
  });
  it("an admin's request goes to Reputation with their own token and the page limit", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [], nextCursor: null }));
    await Page(search());
    expect(resolved).toHaveBeenCalledWith("reputation");
    expect(String(backend.mock.calls[0][0])).toBe("http://reputation.test/api/v1/admin/suspicious-pairs?limit=20");
    expect((backend.mock.calls[0][1] as RequestInit).headers).toMatchObject({ Authorization: `Bearer ${cookie}` });
  });
  it("a cursor is forwarded when it is opaque base64url with dots; anything else is dropped", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [] }));
    await Page(search({ cursor: "ZGVtby1j.0a1b2c" }));
    expect(String(backend.mock.calls[0][0])).toBe("http://reputation.test/api/v1/admin/suspicious-pairs?limit=20&cursor=ZGVtby1j.0a1b2c");
    await Page(search({ cursor: "x&limit=999" }));
    expect(String(backend.mock.calls[1][0])).toBe("http://reputation.test/api/v1/admin/suspicious-pairs?limit=20");
  });
  it("a 403 from Reputation is the forbidden page and a 5xx is 'unavailable'", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 403 }));
    expect(JSON.stringify(await Page(search()))).toContain("forbidden");
    backend.mockResolvedValue(new Response(null, { status: 503 }));
    expect(JSON.stringify(await Page(search()))).toContain("unavailable");
  });
  it("the pairs are rendered as ids and counts", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [{ pairKey: "demo-c:demo-p", clientUserId: "demo-c", practitionerId: "demo-p", bookings: 4, reviews: 3 }] }));
    const out = JSON.stringify(await Page(search()));
    expect(out).toContain("demo-c");
    expect(out).toContain("demo-p");
  });
  it("nextCursor drives the 'more' link, and no cursor means no link", async () => {
    cookie = await token(["Admin"]);
    const hrefs: string[] = [];
    const walk = (n: unknown): void => {
      if (Array.isArray(n)) return n.forEach(walk);
      if (!n || typeof n !== "object") return;
      const props = (n as { props?: { href?: unknown; children?: unknown } }).props;
      if (typeof props?.href === "string") hrefs.push(props.href);
      walk(props?.children);
    };
    backend.mockResolvedValue(Response.json({ items: [], nextCursor: "NEXT.1_a-b" }));
    walk(await Page(search()));
    expect(hrefs).toContain("/admin/podejrzane?cursor=NEXT.1_a-b");
    hrefs.length = 0;
    backend.mockResolvedValue(Response.json({ items: [] }));
    walk(await Page(search()));
    expect(hrefs.filter((h) => h.includes("cursor"))).toEqual([]);
  });
});
