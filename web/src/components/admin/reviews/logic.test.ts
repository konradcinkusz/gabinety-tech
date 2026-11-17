import { describe, expect, it } from "vitest";
import { REASON_MAX, reasonProblem, runDecision } from "./api";
import { cursorOf, statusOf } from "./server";

describe("reasonProblem: the API wants 1 to 500 characters after trimming", () => {
  it.each([["a", null], ["  x  ", null], ["a".repeat(REASON_MAX), null], ["", "invalid"], ["   ", "invalid"], ["a".repeat(REASON_MAX + 1), "invalid"]])(
    "%j -> %s", (raw, out) => expect(reasonProblem(raw)).toBe(out));
});
describe("statusOf / cursorOf", () => {
  it("defaults to Held and accepts only the three statuses", () => {
    expect(statusOf(undefined)).toBe("Held");
    expect(statusOf("Published")).toBe("Published");
    expect(statusOf("Rejected")).toBe("Rejected");
    expect(statusOf("Held&limit=9")).toBe("Held");
  });
  it("cursor is opaque base64url or dropped", () => {
    expect(cursorOf("abc_-1")).toBe("abc_-1");
    expect(cursorOf("a b")).toBeUndefined();
    expect(cursorOf(["x"])).toBeUndefined();
  });
});
describe("runDecision", () => {
  it("publish posts no body; reject posts the trimmed reason", async () => {
    const calls: [string, RequestInit][] = [];
    const f = (async (u: string, i: RequestInit) => (calls.push([u, i]), new Response(null, { status: 204 }))) as unknown as typeof fetch;
    expect(await runDecision("demo-r1", "publish", undefined, f)).toEqual({ ok: true });
    expect(await runDecision("demo-r1", "reject", "  spam  ", f)).toEqual({ ok: true });
    expect(calls[0][0]).toBe("/api/proxy/reputation/api/v1/admin/reviews/demo-r1/publish");
    expect(calls[0][1].body).toBeUndefined();
    expect(calls[1][0]).toBe("/api/proxy/reputation/api/v1/admin/reviews/demo-r1/reject");
    expect(JSON.parse(String(calls[1][1].body))).toEqual({ reason: "spam" });
  });
  it("maps a problem response and a network failure", async () => {
    const p = (async () => Response.json({ code: "invalid_transition" }, { status: 409 })) as unknown as typeof fetch;
    expect(await runDecision("x", "publish", undefined, p)).toEqual({ ok: false, status: 409, code: "invalid_transition" });
    const n = (async () => { throw new Error("boom"); }) as unknown as typeof fetch;
    expect(await runDecision("x", "publish", undefined, n)).toEqual({ ok: false, status: 0, code: "network" });
  });
});
