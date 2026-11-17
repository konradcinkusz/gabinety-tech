import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { resetBackendMemory } from "../api/backends";
import { resetBackendFetch, setBackendFetch } from "../api/http";
import { getTaxonomy, listPractitioners, DirectoryUnavailableError } from "./publicApi";
import { taxonomy } from "./testFixtures";

type Step = Response | Error;
function script(steps: Step[]) {
  const calls: URL[] = [];
  setBackendFetch(async (input) => {
    calls.push(new URL(String(input)));
    const s = steps[Math.min(calls.length - 1, steps.length - 1)]!;
    if (s instanceof Error) throw s;
    return s.clone();
  });
  return calls;
}
const tooMany = (retryAfter?: string) => new Response("{}", { status: 429, headers: retryAfter ? { "retry-after": retryAfter } : {} });

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

const failure = async (p: Promise<unknown>) => {
  const caught = p.then(() => null, (e: unknown) => e);
  await vi.advanceTimersByTimeAsync(5_000);
  return caught;
};

describe("Directory 429 handling", () => {
  it("429 then 200: retries once and returns the data", async () => {
    const calls = script([tooMany(), Response.json(taxonomy)]);
    const p = getTaxonomy();
    await vi.advanceTimersByTimeAsync(2_000);
    await expect(p).resolves.toEqual(taxonomy);
    expect(calls).toHaveLength(2);
  });

  it("honours Retry-After (seconds) and waits for it", async () => {
    const calls = script([tooMany("1"), Response.json(taxonomy)]);
    const p = getTaxonomy();
    await vi.advanceTimersByTimeAsync(900);
    expect(calls).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(200);
    await expect(p).resolves.toEqual(taxonomy);
    expect(calls).toHaveLength(2);
  });

  it("bounds a long Retry-After to 2 seconds", async () => {
    const calls = script([tooMany("120"), Response.json(taxonomy)]);
    const p = getTaxonomy();
    await vi.advanceTimersByTimeAsync(2_001);
    await expect(p).resolves.toEqual(taxonomy);
    expect(calls).toHaveLength(2);
  });

  it("persistent 429: one retry only, then DirectoryUnavailableError(429)", async () => {
    const calls = script([tooMany()]);
    const e = await failure(getTaxonomy());
    expect(e).toBeInstanceOf(DirectoryUnavailableError);
    expect((e as DirectoryUnavailableError).status).toBe(429);
    expect(calls).toHaveLength(2);
  });

  it("list reads retry too", async () => {
    const calls = script([tooMany(), Response.json({ items: [] })]);
    const p = listPractitioners({});
    await vi.advanceTimersByTimeAsync(2_000);
    await expect(p).resolves.toEqual({ items: [] });
    expect(calls).toHaveLength(2);
  });
});

describe("other Directory failures", () => {
  it("5xx is not retried and carries the status only", async () => {
    const calls = script([new Response("secret upstream body demo-user@example.test", { status: 502 })]);
    const e = (await failure(getTaxonomy())) as DirectoryUnavailableError;
    expect(e).toBeInstanceOf(DirectoryUnavailableError);
    expect(e.message).toBe("directory_unavailable:502");
    expect(calls).toHaveLength(1);
  });

  it("a timeout / network error is DirectoryUnavailableError(0), not retried, without upstream detail", async () => {
    const calls = script([new DOMException("timed out for demo-user@example.test", "TimeoutError")]);
    const e = (await failure(getTaxonomy())) as DirectoryUnavailableError;
    expect(e).toBeInstanceOf(DirectoryUnavailableError);
    expect(e.message).toBe("directory_unavailable:0");
    expect(calls).toHaveLength(1);
  });

  it("a timeout on the retry is also DirectoryUnavailableError(0)", async () => {
    script([tooMany(), new DOMException("timeout", "TimeoutError")]);
    const e = (await failure(getTaxonomy())) as DirectoryUnavailableError;
    expect(e.status).toBe(0);
  });
});
