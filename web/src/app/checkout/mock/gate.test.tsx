import { afterEach, describe, expect, it, vi } from "vitest";
import Page from "./[bookingId]/page";

afterEach(() => vi.unstubAllEnvs());
const params = Promise.resolve({ bookingId: "demo-b1" });

describe("/checkout/mock/[bookingId]", () => {
  it.each(["Stripe", ""])("is a 404 when payments are not Mock (provider %j)", async (provider) => {
    vi.stubEnv("Payments__Provider", provider);
    vi.stubEnv("GABINETY_FAKE", "");
    await expect(Page({ params })).rejects.toMatchObject({ digest: expect.stringContaining("404") });
  });
  it("is a 404 outside the fake profile even when the provider says Mock (F-01)", async () => {
    vi.stubEnv("Payments__Provider", "Mock");
    vi.stubEnv("GABINETY_FAKE", "");
    vi.stubEnv("Environment__Kind", "");
    await expect(Page({ params })).rejects.toMatchObject({ digest: expect.stringContaining("404") });
  });
  it("renders in the fake profile with payments Mock", async () => {
    vi.stubEnv("Payments__Provider", "Mock");
    vi.stubEnv("GABINETY_FAKE", "1");
    expect(await Page({ params })).toBeTruthy();
  });
});
