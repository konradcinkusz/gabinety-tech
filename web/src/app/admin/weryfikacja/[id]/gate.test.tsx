import { SignJWT } from "jose";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

let cookie: string | undefined;
vi.mock("next/headers", () => ({ cookies: async () => ({ get: (n: string) => (n === "gt_at" && cookie ? { value: cookie } : undefined) }) }));
const backend = vi.fn();
vi.mock("@/lib/api/backends", () => ({ resolveBackend: async () => "http://directory.test", forgetBackend: () => {} }));
vi.mock("@/lib/api/http", () => ({ backendFetch: (...a: unknown[]) => backend(...a) }));
vi.mock("next/navigation", () => ({
  notFound: () => { throw Object.assign(new Error("NEXT_NOT_FOUND"), { digest: "NEXT_HTTP_ERROR_FALLBACK;404" }); },
  redirect: (u: string) => { throw Object.assign(new Error("NEXT_REDIRECT"), { digest: `NEXT_REDIRECT;replace;${u};307;` }); },
}));

const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const token = (roles: string[]) =>
  new SignJWT({ [ROLE]: roles }).setProtectedHeader({ alg: "HS256" }).setSubject("demo-u").setExpirationTime("1h").sign(new TextEncoder().encode("demo-secret-demo-secret-demo-secret!"));
const ID = "3f2b8c1e-5a4d-4e6f-9a1b-2c3d4e5f6a7b";
const D = { id: ID, slug: "demo-jan", displayName: "Demo Jan", headline: "h", bio: "b", status: "Submitted", githubLinked: false, nipValid: false, createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z" };

beforeEach(() => backend.mockReset());
afterEach(() => { cookie = undefined; });

describe("/admin/weryfikacja/[id] is admin-only at the page and asks Directory with the user's token", async () => {
  const { default: Page } = await import("./page");
  const params = (id: string) => ({ params: Promise.resolve({ id }) });

  it("a signed-out visitor is sent to sign in and the backend is never asked", async () => {
    await expect(Page(params(ID))).rejects.toMatchObject({ digest: expect.stringContaining("/logowanie?redirect=") });
    expect(backend).not.toHaveBeenCalled();
  });
  it("a non-admin gets the forbidden page and the backend is never asked", async () => {
    cookie = await token(["User"]);
    expect(JSON.stringify(await Page(params(ID)))).toContain("forbidden");
    expect(backend).not.toHaveBeenCalled();
  });
  it("an id that is not a guid is a 404 and is never forwarded", async () => {
    cookie = await token(["Admin"]);
    await expect(Page(params("../x?y=1"))).rejects.toMatchObject({ digest: expect.stringContaining("404") });
    expect(backend).not.toHaveBeenCalled();
  });
  it("an admin: both endpoints are called with the bearer token", async () => {
    cookie = await token(["Admin"]);
    backend.mockImplementation(async (u: string) => (String(u).endsWith("/verification-events") ? Response.json({ items: [] }) : Response.json(D)));
    await Page(params(ID));
    const urls = backend.mock.calls.map((c) => String(c[0])).sort();
    expect(urls).toEqual([`http://directory.test/api/v1/admin/practitioners/${ID}`, `http://directory.test/api/v1/admin/practitioners/${ID}/verification-events`]);
    for (const c of backend.mock.calls) expect((c[1] as RequestInit).headers).toMatchObject({ Authorization: `Bearer ${cookie}` });
  });
  it("404 from the backend is a 404 page", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 404 }));
    await expect(Page(params(ID))).rejects.toMatchObject({ digest: expect.stringContaining("404") });
  });
  it("403 from the backend is the forbidden page", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 403 }));
    expect(JSON.stringify(await Page(params(ID)))).toContain("forbidden");
  });
  it("detail ok but events failing: the page renders with the timeline marked unavailable", async () => {
    cookie = await token(["Admin"]);
    backend.mockImplementation(async (u: string) => (String(u).endsWith("/verification-events") ? new Response(null, { status: 500 }) : Response.json(D)));
    const out = JSON.stringify(await Page(params(ID)));
    expect(out).toContain("Demo Jan");
    expect(out).toContain('"events":null');
  });
  it("detail failing is an unavailable alert, not a crash", async () => {
    cookie = await token(["Admin"]);
    backend.mockResolvedValue(new Response(null, { status: 500 }));
    expect(JSON.stringify(await Page(params(ID)))).toContain("unavailable");
  });
});
