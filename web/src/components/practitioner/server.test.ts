import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetBackendFetch } from "../../lib/api/http";
import { resetBackendMemory } from "../../lib/api/backends";
import { fakeBackend } from "../../lib/api/testUtils";
import { loadAvailability, loadOfferings, loadProfile } from "./server";

beforeEach(() => {
  vi.stubEnv("Backend__Directory", "http://directory.internal");
  vi.stubEnv("Backend__Booking", "http://booking.internal");
  resetBackendMemory();
});
afterEach(() => {
  vi.unstubAllEnvs();
  resetBackendFetch();
});

describe("server loaders", () => {
  it("send the user's token to the right backend and return what the API said", async () => {
    const calls = fakeBackend(() => Response.json({ headline: "Demo" }));
    expect(await loadProfile("tok-1")).toEqual({ state: "ready", value: { headline: "Demo" } });
    expect(calls[0].url).toBe("http://directory.internal/api/v1/me/practitioner");
    expect(calls[0].headers.get("authorization")).toBe("Bearer tok-1");
  });
  it("a user without a profile is 'none' (Directory 404, Booking 403), not an error", async () => {
    fakeBackend(() => new Response("{}", { status: 404 }));
    expect(await loadProfile("t")).toEqual({ state: "none" });
    fakeBackend(() => new Response("{}", { status: 403 }));
    expect(await loadAvailability("t")).toEqual({ state: "none" });
  });
  it("a failing or unreachable service is 'unavailable'", async () => {
    fakeBackend(() => new Response("{}", { status: 503 }));
    expect(await loadOfferings("t")).toEqual({ state: "unavailable" });
    fakeBackend(() => { throw new Error("down"); });
    expect(await loadProfile("t")).toEqual({ state: "unavailable" });
  });
  it("offerings come from `items`", async () => {
    fakeBackend(() => Response.json({ items: [{ id: "demo-1" }] }));
    expect(await loadOfferings("t")).toEqual({ state: "ready", value: [{ id: "demo-1" }] });
  });
});
