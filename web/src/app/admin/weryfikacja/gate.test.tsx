import { SignJWT } from "jose";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

let cookie: string | undefined;
vi.mock("next/headers", () => ({ cookies: async () => ({ get: (n: string) => (n === "gt_at" && cookie ? { value: cookie } : undefined) }) }));
const backend = vi.fn();
vi.mock("@/lib/api/backends", () => ({ resolveBackend: async () => "http://directory.test", forgetBackend: () => {} }));
vi.mock("@/lib/api/http", () => ({ backendFetch: (...a: unknown[]) => backend(...a) }));

const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const token = (roles: string[]) =>
  new SignJWT({ [ROLE]: roles }).setProtectedHeader({ alg: "HS256" }).setSubject("demo-u").setExpirationTime("1h").sign(new TextEncoder().encode("demo-secret-demo-secret-demo-secret!"));

beforeEach(() => backend.mockReset());
afterEach(() => { cookie = undefined; });

describe("/admin/weryfikacja is admin-only at the page, not only at the edge", async () => {
  const { default: Page } = await import("./page");
  const search = (o: Record<string, string> = {}) => ({ searchParams: Promise.resolve(o) });

  it("a signed-out visitor is sent to the sign-in page with the way back", async () => {
    await expect(Page(search())).rejects.toMatchObject({ digest: expect.stringContaining("/logowanie?redirect=") });
    expect(backend).not.toHaveBeenCalled();
  });
  it("a signed-in non-admin gets the forbidden page and the backend is never asked", async () => {
    cookie = await token(["User"]);
    const out = JSON.stringify(await Page(search()));
    expect(out).toContain("forbidden");
    expect(backend).not.toHaveBeenCalled();
  });
  it("an admin sees the queue, loaded with their token and status Submitted by default", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [], nextCursor: null }));
    await Page(search());
    expect(String(backend.mock.calls[0][0])).toBe("http://directory.test/api/v1/admin/practitioners?status=Submitted&limit=12");
    expect((backend.mock.calls[0][1] as RequestInit).headers).toMatchObject({ Authorization: `Bearer ${cookie}` });
  });
  it("a 403 from the backend is the forbidden page too", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 403 }));
    expect(JSON.stringify(await Page(search()))).toContain("forbidden");
  });
  it("an unknown status filter falls back to Submitted (it is never forwarded)", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(Response.json({ items: [] }));
    await Page(search({ status: "x&limit=999" }));
    expect(String(backend.mock.calls[0][0])).toContain("status=Submitted&limit=12");
  });
});
