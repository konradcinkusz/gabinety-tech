import { describe, expect, it } from "vitest";
import config from "../../../next.config";
import { buildCsp, staticHeaders } from "./headers";

const directive = (csp: string, name: string) => csp.split("; ").find((d) => d.startsWith(name + " ") || d === name);

describe("buildCsp", () => {
  const csp = buildCsp("abc123");
  it("carries the required restrictive directives", () => {
    expect(directive(csp, "default-src")).toBe("default-src 'self'");
    expect(directive(csp, "frame-ancestors")).toBe("frame-ancestors 'none'");
    expect(directive(csp, "base-uri")).toBe("base-uri 'self'");
    expect(directive(csp, "form-action")).toBe("form-action 'self'");
    expect(directive(csp, "object-src")).toBe("object-src 'none'");
    expect(directive(csp, "connect-src")).toBe("connect-src 'self'");
    expect(directive(csp, "img-src")).toBeDefined();
    expect(directive(csp, "font-src")).toBeDefined();
  });
  it("uses the nonce for scripts and has no unsafe-eval or unsafe-inline in production", () => {
    expect(directive(csp, "script-src")).toBe("script-src 'self' 'nonce-abc123' 'strict-dynamic'");
    expect(csp).not.toContain("unsafe-eval");
    expect(csp).not.toContain("unsafe-inline");
  });
  it("allows unsafe-eval in development only", () => {
    expect(buildCsp("n", true)).toContain("'unsafe-eval'");
  });
  it("allows no remote hosts", () => expect(csp).not.toMatch(/https?:/));
});

describe("staticHeaders and next.config headers()", () => {
  const byKey = (h: { key: string; value: string }[]) => Object.fromEntries(h.map((x) => [x.key, x.value]));
  it("sets nosniff, referrer, permissions and frame headers", () => {
    const h = byKey(staticHeaders(false));
    expect(h["X-Content-Type-Options"]).toBe("nosniff");
    expect(h["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(h["X-Frame-Options"]).toBe("DENY");
    for (const f of ["camera", "microphone", "geolocation"]) expect(h["Permissions-Policy"]).toContain(`${f}=()`);
  });
  it("sends HSTS in production only", () => {
    expect(byKey(staticHeaders(false))["Strict-Transport-Security"]).toBeUndefined();
    expect(byKey(staticHeaders(true))["Strict-Transport-Security"]).toContain("max-age=");
  });
  it("applies the table to every route and locks down /api", async () => {
    const rules = await config.headers!();
    const all = rules.find((r) => r.source === "/:path*")!;
    expect(all.headers.map((x) => x.key)).toContain("X-Content-Type-Options");
    const api = rules.find((r) => r.source === "/api/:path*")!;
    expect(byKey(api.headers)["Content-Security-Policy"]).toContain("default-src 'none'");
  });
});
