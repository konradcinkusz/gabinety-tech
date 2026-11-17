import { SignJWT } from "jose";
import { afterEach, describe, expect, it, vi } from "vitest";

let cookie: string | undefined;
vi.mock("next/headers", () => ({ cookies: async () => ({ get: (n: string) => (n === "gt_at" && cookie ? { value: cookie } : undefined) }) }));

const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const token = (roles: string[]) =>
  new SignJWT({ [ROLE]: roles }).setProtectedHeader({ alg: "HS256" }).setSubject("demo-u").setExpirationTime("1h").sign(new TextEncoder().encode("demo-secret-demo-secret-demo-secret!"));
afterEach(() => { cookie = undefined; });

const hrefs = (n: unknown, out: string[] = []): string[] => {
  if (Array.isArray(n)) n.forEach((x) => hrefs(x, out));
  else if (n && typeof n === "object") {
    const p = (n as { props?: { href?: unknown; children?: unknown } }).props;
    if (typeof p?.href === "string") out.push(p.href);
    hrefs(p?.children, out);
  }
  return out;
};

const variants = (n: unknown, out: unknown[] = []): unknown[] => {
  if (Array.isArray(n)) n.forEach((x) => variants(x, out));
  else if (n && typeof n === "object") {
    const p = (n as { props?: { variant?: unknown; children?: unknown } }).props;
    out.push(p?.variant);
    variants(p?.children, out);
  }
  return out;
};

describe("/admin/ksiega is admin-only at the page", async () => {
  const { default: Page } = await import("./page");
  const search = (o: { from?: string; to?: string } = {}) => ({ searchParams: Promise.resolve(o) });

  it("a signed-out visitor is sent to sign in with the way back", async () => {
    await expect(Page(search())).rejects.toMatchObject({ digest: expect.stringContaining("/logowanie?redirect=") });
  });
  it("a signed-in non-admin gets 'forbidden', no download link", async () => {
    cookie = await token(["User"]);
    const out = await Page(search());
    expect(JSON.stringify(out)).toContain("forbidden");
    expect(hrefs(out)).toEqual([]);
  });
  it("an admin's download link carries the chosen dates", async () => {
    cookie = await token(["Admin"]);
    expect(hrefs(await Page(search({ from: "2026-01-01", to: "2026-03-31" })))).toContain("/admin/ksiega/csv?from=2026-01-01&to=2026-03-31");
  });
  it("an invalid period shows the problem and offers no download", async () => {
    cookie = await token(["Admin"]);
    for (const q of [{ from: "2026-01-01", to: "2027-06-01" }, { from: "2026-03-02", to: "2026-03-01" }, { from: "x" }]) {
      const out = await Page(search(q));
      expect(hrefs(out).some((h) => h.startsWith("/admin/ksiega/csv"))).toBe(false);
      expect(variants(out)).toContain("danger");
    }
  });
  it("without a query the period is the current month", async () => {
    cookie = await token(["Admin"]);
    const csv = hrefs(await Page(search())).find((h) => h.startsWith("/admin/ksiega/csv"))!;
    const p = new URL(csv, "http://x").searchParams;
    expect(p.get("from")).toMatch(/^\d{4}-\d{2}-01$/);
    expect(p.get("to")! >= p.get("from")!).toBe(true);
  });
});
