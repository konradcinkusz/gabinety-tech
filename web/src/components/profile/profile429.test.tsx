import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetBackendFetch, setBackendFetch } from "@/lib/api/http";
import { resetBackendMemory } from "@/lib/api/backends";
import { getProfile } from "./profileApi";
import { getRankingRules } from "../static/rankingApi";

beforeEach(() => {
  process.env["Backend__Directory"] = "http://directory.test";
  resetBackendMemory();
  vi.useFakeTimers();
});
afterEach(() => {
  vi.useRealTimers();
  resetBackendFetch();
  resetBackendMemory();
  delete process.env["Backend__Directory"];
});

describe.each([
  ["profile", () => getProfile("demo-programista-1")],
  ["ranking rules", () => getRankingRules()],
])("%s read", (_n, read) => {
  it("retries a 429 once and succeeds", async () => {
    let n = 0;
    setBackendFetch(async () => (++n === 1 ? new Response("{}", { status: 429, headers: { "retry-after": "1" } }) : Response.json({ offerings: [] })));
    const p = read();
    await vi.advanceTimersByTimeAsync(1_000);
    await expect(p).resolves.toBeTruthy();
    expect(n).toBe(2);
  });

  it("persistent 429 throws directory_unavailable:429 after one retry", async () => {
    let n = 0;
    setBackendFetch(async () => (++n, new Response("{}", { status: 429 })));
    const p = read().then(() => null, (e: Error) => e);
    await vi.advanceTimersByTimeAsync(5_000);
    expect(((await p) as Error).message).toBe("directory_unavailable:429");
    expect(n).toBe(2);
  });
});
