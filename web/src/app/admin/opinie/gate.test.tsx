import { SignJWT } from "jose";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

let cookie: string | undefined;
vi.mock("next/headers", () => ({ cookies: async () => ({ get: (n: string) => (n === "gt_at" && cookie ? { value: cookie } : undefined) }) }));
const backend = vi.fn();
vi.mock("@/lib/api/backends", () => ({ resolveBackend: async () => "http://reputation.test", forgetBackend: () => {} }));
vi.mock("@/lib/api/http", () => ({ backendFetch: (...a: unknown[]) => backend(...a) }));

const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const token = (roles: string[]) =>
  new SignJWT({ [ROLE]: roles }).setProtectedHeader({ alg: "HS256" }).setSubject("demo-u").setExpirationTime("1h").sign(new TextEncoder().encode("demo-secret-demo-secret-demo-secret!"));

beforeEach(() => backend.mockReset());
afterEach(() => { cookie = undefined; });

describe("/admin/opinie is admin-only at the page, not only at the edge", async () => {
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
  it("an admin gets the held queue by default, with their own token", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [], nextCursor: null }));
    await Page(search());
    expect(String(backend.mock.calls[0][0])).toBe("http://reputation.test/api/v1/admin/reviews?status=Held&limit=20");
    expect((backend.mock.calls[0][1] as RequestInit).headers).toMatchObject({ Authorization: `Bearer ${cookie}` });
  });
  it("status and cursor are forwarded; anything else is not", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [] }));
    await Page(search({ status: "Published", cursor: "abc_-1" }));
    expect(String(backend.mock.calls[0][0])).toBe("http://reputation.test/api/v1/admin/reviews?status=Published&limit=20&cursor=abc_-1");
    await Page(search({ status: "x&limit=999" }));
    expect(String(backend.mock.calls[1][0])).toContain("status=Held&limit=20");
  });
  it("a 403 from Reputation is the forbidden page", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 403 }));
    expect(JSON.stringify(await Page(search()))).toContain("forbidden");
  });
  it("an unavailable backend is a message, not a list", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 503 }));
    const out = (await Page(search())) as unknown as { props: { children: { props: { "data-testid"?: string } }[] } };
    expect(out.props.children.at(-1)?.props["data-testid"]).toBe("unavailable");
  });
  it("nextCursor drives the 'more' link", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [], nextCursor: "NEXT_1" }));
    const hrefs: string[] = [];
    const walk = (n: unknown): void => {
      if (Array.isArray(n)) return n.forEach(walk);
      if (!n || typeof n !== "object") return;
      const props = (n as { props?: { href?: unknown; children?: unknown } }).props;
      if (typeof props?.href === "string") hrefs.push(props.href);
      walk(props?.children);
    };
    walk(await Page(search()));
    expect(hrefs).toContain("/admin/opinie?status=Held&cursor=NEXT_1");
  });
});
