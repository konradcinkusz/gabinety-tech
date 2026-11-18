import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { GET, POST } from "../../app/api/proxy/[...path]/route";
import { resetBackendFetch } from "./http";
import { resetBackendMemory } from "./backends";
import { fakeBackend } from "./testUtils";

const SITE = "http://localhost:3000";
const ctx = (path: string) => ({ params: Promise.resolve({ path: path.split("/") }) });
const get = (path: string, headers: Record<string, string> = {}) => GET(new Request(SITE + "/api/proxy/" + path, { headers }), ctx(path));

beforeEach(() => {
  vi.stubEnv("Backend__Directory", "http://directory.internal:5101");
  vi.stubEnv("Site__BaseUrl", SITE);
});
afterEach(() => {
  vi.unstubAllEnvs();
  resetBackendFetch();
  resetBackendMemory();
});

describe("the visitor's address is forwarded to the services for their rate limit", () => {
  it("sends X-Client-Address and the trusted proxy key when both are known", async () => {
    vi.stubEnv("RateLimit__TrustedProxyKey", "demo-edge-key");
    const calls = fakeBackend(() => Response.json({}));
    await get("directory/api/v1/practitioners", { "fly-client-ip": "203.0.113.5" });
    expect(calls[0].headers.get("x-client-address")).toBe("203.0.113.5");
    expect(calls[0].headers.get("x-edge-key")).toBe("demo-edge-key");
  });
  it("sends neither when no key is configured: an address nobody will trust is not sent", async () => {
    const calls = fakeBackend(() => Response.json({}));
    await get("directory/api/v1/practitioners", { "fly-client-ip": "203.0.113.5" });
    expect(calls[0].headers.get("x-client-address")).toBeNull();
    expect(calls[0].headers.get("x-edge-key")).toBeNull();
  });
  it("sends neither when the address is not a valid one (the service then uses the web machine's)", async () => {
    vi.stubEnv("RateLimit__TrustedProxyKey", "demo-edge-key");
    const calls = fakeBackend(() => Response.json({}));
    await get("directory/api/v1/practitioners", { "x-forwarded-for": "junk" });
    expect(calls[0].headers.get("x-client-address")).toBeNull();
    expect(calls[0].headers.get("x-edge-key")).toBeNull();
  });
  it("never passes through what the browser put in those headers", async () => {
    const calls = fakeBackend(() => Response.json({}));
    await get("directory/api/v1/practitioners", { "x-client-address": "6.6.6.6", "x-edge-key": "guess" });
    expect(calls[0].headers.get("x-client-address")).toBeNull();
    expect(calls[0].headers.get("x-edge-key")).toBeNull();
    vi.stubEnv("RateLimit__TrustedProxyKey", "demo-edge-key");
    await get("directory/api/v1/practitioners", { "x-client-address": "6.6.6.6", "x-edge-key": "guess", "fly-client-ip": "203.0.113.5" });
    expect(calls[1].headers.get("x-client-address")).toBe("203.0.113.5");
    expect(calls[1].headers.get("x-edge-key")).toBe("demo-edge-key");
  });
  it("also on writes, and the key never reaches an internal path as an internal key", async () => {
    vi.stubEnv("RateLimit__TrustedProxyKey", "demo-edge-key");
    const calls = fakeBackend(() => Response.json({}));
    await POST(new Request(SITE + "/api/proxy/directory/internal/v1/x", { method: "POST", headers: { "fly-client-ip": "203.0.113.5", "content-type": "application/json" }, body: "{}" }), ctx("directory/internal/v1/x"));
    expect(calls[0].headers.get("x-edge-key")).toBe("demo-edge-key");
    expect(calls[0].headers.get("x-internal-key")).toBeNull(); // a different secret that opens no route
  });
});
