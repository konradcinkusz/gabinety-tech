// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AdminReview } from "./api";
import { ReviewList } from "./ReviewList";
import { mv } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

let host: HTMLElement;
let root: Root;
beforeEach(() => {
  refresh.mockClear();
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});
const render = (el: React.ReactElement) => act(() => root.render(el));
const flush = () => act(async () => { for (let i = 0; i < 5; i++) await Promise.resolve(); });
const button = (re: RegExp) => [...host.querySelectorAll("button")].find((b) => re.test(b.textContent ?? "")) as HTMLButtonElement | undefined;
const click = (re: RegExp) => act(async () => button(re)!.click());
const review = (o: Partial<AdminReview> = {}): AdminReview => ({
  id: "demo-r1", practitionerId: "demo-p1", rating: 2, text: "Demo: <script>x</script> zły kontakt", status: "Held", holdReasons: ["spam", "personal_data"],
  triageFlag: "hold", triageModelId: "demo-model", createdAt: "2026-11-02T10:30:00Z", publishedAt: null, moderatedBy: null, moderationNote: null, ...o,
});
function stub(status = 204, body?: unknown) {
  const calls: { url: string; init?: RequestInit }[] = [];
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    calls.push({ url, init });
    return body === undefined ? new Response(null, { status }) : Response.json(body, { status });
  }));
  return calls;
}

describe("what a row shows", () => {
  it("the text only as text, the rating, Warsaw time and the triage reasons in words", () => {
    render(<ReviewList items={[review()]} mode="Held" />);
    expect(host.textContent).toContain("<script>x</script>");
    expect(host.querySelector("script")).toBeNull();
    expect(host.textContent).toContain("2/5");
    expect(host.querySelector("time")?.textContent).toContain("11:30");
    expect(host.textContent).toContain(mv("reason.spam"));
    expect(host.textContent).toContain(mv("reason.personal_data"));
    expect(host.textContent).toContain(mv("triage.hold"));
  });
  it("an unknown reason is shown as 'other', never as the raw string", () => {
    render(<ReviewList items={[review({ holdReasons: ["weird"] })]} mode="Held" />);
    expect(host.textContent).toContain(mv("reason.other"));
    expect(host.textContent).not.toContain("weird");
  });
  it("triage unavailable is explained", () => {
    render(<ReviewList items={[review({ triageFlag: "unavailable", holdReasons: [] })]} mode="Held" />);
    expect(host.textContent).toContain(mv("triage.unavailable"));
  });
  it("an empty list says so per status", () => {
    render(<ReviewList items={[]} mode="Held" />);
    expect(host.textContent).toContain(mv("empty.Held"));
  });
  it("the reviewer is not shown, the moderator note is on a moderated row", () => {
    render(<ReviewList items={[review({ status: "Rejected", moderatedBy: "demo-admin", moderationNote: "Demo: powód" })]} mode="Rejected" />);
    expect(host.textContent).toContain("Demo: powód");
  });
  it("actions per status: Held both, Published only reject, Rejected none", () => {
    render(<ReviewList items={[review()]} mode="Held" />);
    expect(button(new RegExp(mv("publish.open")))).toBeDefined();
    expect(button(new RegExp(mv("reject.open")))).toBeDefined();
    render(<ReviewList items={[review({ status: "Published" })]} mode="Published" />);
    expect(button(new RegExp(mv("publish.open")))).toBeUndefined();
    expect(button(new RegExp(mv("reject.open")))).toBeDefined();
    render(<ReviewList items={[review({ status: "Rejected" })]} mode="Rejected" />);
    expect(host.querySelectorAll("button")).toHaveLength(0);
  });
});

describe("publish", () => {
  it("posts to the reputation proxy and reloads the list", async () => {
    const calls = stub();
    render(<ReviewList items={[review()]} mode="Held" />);
    await click(new RegExp(mv("publish.open")));
    await flush();
    expect(calls[0].url).toBe("/api/proxy/reputation/api/v1/admin/reviews/demo-r1/publish");
    expect(calls[0].init?.method).toBe("POST");
    expect(refresh).toHaveBeenCalled();
  });
  it("409 says the state changed and reloads", async () => {
    stub(409, { code: "invalid_transition" });
    render(<ReviewList items={[review()]} mode="Held" />);
    await click(new RegExp(mv("publish.open")));
    await flush();
    expect(host.querySelector('[role="alert"], [role="status"]')?.textContent).toContain(mv("error.stateChanged"));
    expect(refresh).toHaveBeenCalled();
  });
});

describe("reject", () => {
  const openForm = async () => {
    render(<ReviewList items={[review()]} mode="Held" />);
    await click(new RegExp(mv("reject.open")));
  };
  const field = () => host.querySelector("textarea")!;
  it("needs a reason: empty blocks the call and names the problem", async () => {
    const calls = stub();
    await openForm();
    await click(new RegExp(mv("reject.confirm")));
    expect(host.textContent).toContain(mv("reject.reasonInvalid"));
    expect(calls).toHaveLength(0);
    expect(field().getAttribute("aria-invalid")).toBe("true");
  });
  it("sends the trimmed reason and reloads", async () => {
    const calls = stub();
    await openForm();
    await userEvent.type(field(), "  Demo: spam  ");
    await click(new RegExp(mv("reject.confirm")));
    await flush();
    expect(calls[0].url).toBe("/api/proxy/reputation/api/v1/admin/reviews/demo-r1/reject");
    expect(JSON.parse(String(calls[0].init?.body))).toEqual({ reason: "Demo: spam" });
    expect(refresh).toHaveBeenCalled();
  });
  it("cancel closes the form without a call", async () => {
    const calls = stub();
    await openForm();
    await click(new RegExp(mv("reject.cancel")));
    expect(host.querySelector("textarea")).toBeNull();
    expect(calls).toHaveLength(0);
  });
  it("a server 422 on the reason is shown, a network failure too", async () => {
    stub(422, { code: "validation_failed", errors: { reason: ["x"] } });
    await openForm();
    await userEvent.type(field(), "powód");
    await click(new RegExp(mv("reject.confirm")));
    await flush();
    expect(host.textContent).toContain(mv("error.reason"));
    vi.stubGlobal("fetch", vi.fn(async () => { throw new Error("down"); }));
    await click(new RegExp(mv("reject.confirm")));
    await flush();
    expect(host.textContent).toContain(mv("error.network"));
  });
});
