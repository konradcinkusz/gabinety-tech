import { describe, expect, it } from "vitest";
import { checkOrigin } from "./originGuard";

const SITE = "http://localhost:3000";
const req = (method: string, headers: Record<string, string> = {}) =>
  new Request("http://localhost:3000/api/auth/login", { method, headers });

describe("originGuard", () => {
  it("rejects a foreign Origin on POST with 403 cross_site_request", async () => {
    const res = checkOrigin(req("POST", { Origin: "https://evil.example.test" }), SITE);
    expect(res?.status).toBe(403);
    expect(res?.headers.get("content-type")).toContain("application/problem+json");
    expect(await res?.json()).toMatchObject({
      type: "urn:gabinety:error:cross_site_request",
      code: "cross_site_request",
      status: 403,
    });
  });
  it("lets a same-origin POST pass", () => {
    expect(checkOrigin(req("POST", { Origin: SITE }), SITE)).toBeNull();
  });
  it("rejects Sec-Fetch-Site cross-site even with no Origin", () => {
    expect(checkOrigin(req("POST", { "Sec-Fetch-Site": "cross-site" }), SITE)?.status).toBe(403);
  });
  it("lets a POST with neither header pass", () => {
    expect(checkOrigin(req("POST"), SITE)).toBeNull();
  });
  it("never guards GET", () => {
    const r = req("GET", { Origin: "https://evil.example.test", "Sec-Fetch-Site": "cross-site" });
    expect(checkOrigin(r, SITE)).toBeNull();
  });
  it.each(["PUT", "PATCH", "DELETE"])("guards %s", (m) => {
    expect(checkOrigin(req(m, { Origin: "https://evil.example.test" }), SITE)?.status).toBe(403);
  });
  it("rejects the opaque Origin: null", () => {
    expect(checkOrigin(req("POST", { Origin: "null" }), SITE)?.status).toBe(403);
  });
});
