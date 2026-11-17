import { SignJWT } from "jose";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const backend = vi.fn();
let base: string | null = "http://booking.test";
vi.mock("@/lib/api/backends", () => ({ resolveBackend: async () => base, forgetBackend: vi.fn() }));
vi.mock("@/lib/api/http", () => ({ backendFetch: (...a: unknown[]) => backend(...a) }));

const ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
const token = (roles: string[]) =>
  new SignJWT({ [ROLE]: roles }).setProtectedHeader({ alg: "HS256" }).setSubject("demo-u").setExpirationTime("1h").sign(new TextEncoder().encode("demo-secret-demo-secret-demo-secret!"));
const get = async (qs: string, t?: string) =>
  (await import("./route")).GET(new Request(`http://web.test/admin/ksiega/csv${qs}`, { headers: t ? { cookie: `gt_at=${t}` } : {} }));
const Q = "?from=2026-01-01&to=2026-03-31";

beforeEach(() => { backend.mockReset(); base = "http://booking.test"; });
afterEach(() => vi.restoreAllMocks());

describe("GET /admin/ksiega/csv (BFF)", () => {
  it("is 401 without a session and 403 for a non-admin; Booking is never asked", async () => {
    expect((await get(Q)).status).toBe(401);
    expect((await get(Q, await token(["User"]))).status).toBe(403);
    expect(backend).not.toHaveBeenCalled();
  });
  it("refuses a bad period before asking Booking", async () => {
    const t = await token(["Admin"]);
    for (const qs of ["", "?from=2026-01-01", "?from=2026-02-30&to=2026-03-01", "?from=2026-03-02&to=2026-03-01", "?from=2026-01-01&to=2027-01-02"]) {
      expect((await get(qs, t)).status, qs).toBe(400);
    }
    expect(backend).not.toHaveBeenCalled();
  });
  it("asks Booking with the user's own token and only the two dates", async () => {
    const t = await token(["Admin"]);
    backend.mockResolvedValue(new Response("h\r\n", { headers: { "content-type": "text/csv" } }));
    await get(Q + "&x=1&limit=9", t);
    expect(String(backend.mock.calls[0][0])).toBe("http://booking.test/api/v1/admin/ledger.csv?from=2026-01-01&to=2026-03-31");
    expect((backend.mock.calls[0][1] as RequestInit).headers).toMatchObject({ Authorization: `Bearer ${t}` });
  });
  it("streams the body through unbuffered, with our own filename and no caching", async () => {
    const t = await token(["Admin"]);
    let pulled = 0;
    const body = new ReadableStream<Uint8Array>({
      pull(c) { pulled++; if (pulled > 3) return c.close(); c.enqueue(new TextEncoder().encode(`row${pulled}\r\n`)); },
    }, { highWaterMark: 0 });
    backend.mockResolvedValue(new Response(body, { headers: { "content-type": "text/csv; charset=utf-8", "content-disposition": 'attachment; filename="evil.csv"', "set-cookie": "a=b" } }));
    const r = await get(Q, t);
    expect(pulled).toBe(0); // nothing read before the client reads
    expect(r.body).toBeInstanceOf(ReadableStream); // a stream, not a buffered copy
    expect(r.headers.get("content-type")).toBe("text/csv; charset=utf-8");
    expect(r.headers.get("content-disposition")).toBe('attachment; filename="ledger-2026-01-01-2026-03-31.csv"');
    expect(r.headers.get("cache-control")).toBe("no-store");
    expect(r.headers.get("set-cookie")).toBeNull();
    expect(await r.text()).toBe("row1\r\nrow2\r\nrow3\r\n");
  });
  it("maps Booking's refusals and outages without echoing them", async () => {
    const t = await token(["Admin"]);
    backend.mockResolvedValueOnce(new Response("secret", { status: 403 }));
    expect((await get(Q, t)).status).toBe(403);
    backend.mockResolvedValueOnce(new Response("secret", { status: 400 }));
    expect((await get(Q, t)).status).toBe(400);
    backend.mockResolvedValueOnce(new Response("secret", { status: 500 }));
    const r = await get(Q, t);
    expect(r.status).toBe(503);
    expect(await r.text()).not.toContain("secret");
    backend.mockRejectedValueOnce(new Error("http://10.0.0.5 refused"));
    const e = await get(Q, t);
    expect(e.status).toBe(503);
    expect(await e.text()).not.toContain("10.0.0.5");
    base = null;
    expect((await get(Q, t)).status).toBe(503);
  });
  it("the timeout guards the response headers, not the body", async () => {
    const t = await token(["Admin"]);
    backend.mockResolvedValue(new Response("h\r\n"));
    await get(Q, t);
    const init = backend.mock.calls[0][1] as RequestInit;
    expect(init.signal).toBeInstanceOf(AbortSignal);
    expect(init.signal!.aborted).toBe(false);
  });
});
