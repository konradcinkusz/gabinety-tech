import { afterEach, describe, expect, it, vi } from "vitest";
import { GET } from "../../app/api/config/route";

afterEach(() => vi.unstubAllEnvs());

describe("GET /api/config", () => {
  it("returns only siteBaseUrl and authEnabled, no secret and no backend URL", async () => {
    vi.stubEnv("Backend__Directory", "http://directory.internal:5101");
    vi.stubEnv("Backend__Auth", "http://auth.internal:5105");
    vi.stubEnv("Site__BaseUrl", "https://gabinety.example.test");
    vi.stubEnv("Identity__Issuer", "issuer-x");
    const text = await (await GET()).text();
    expect(JSON.parse(text)).toEqual({ siteBaseUrl: "https://gabinety.example.test", authEnabled: true });
    for (const bad of ["directory.internal", "auth.internal", "issuer-x", "secret", "key", "token"]) {
      expect(text.toLowerCase()).not.toContain(bad);
    }
  });
  it("reports authEnabled false when no identity backend is configured", async () => {
    vi.stubEnv("Backend__Auth", "");
    expect((await (await GET()).json()).authEnabled).toBe(false);
  });
});
