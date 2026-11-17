// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AdminDispute } from "./api";
import { DisputeList } from "./DisputeList";
import { formatPln } from "@/components/ui";
import { dv } from "./strings";

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
  vi.restoreAllMocks();
});
const render = (el: React.ReactElement) => act(() => root.render(el));
const flush = () => act(async () => { for (let i = 0; i < 5; i++) await Promise.resolve(); });
const button = (re: RegExp) => [...host.querySelectorAll("button")].find((b) => re.test(b.textContent ?? "")) as HTMLButtonElement | undefined;
const dispute = (o: Partial<AdminDispute> = {}): AdminDispute => ({
  bookingId: "demo-b1", clientUserId: "demo-client", practitionerUserId: "demo-practitioner", kind: "not_as_described",
  text: "Demo: <b>konsultacja</b> nie odpowiadała opisowi", openedAt: "2026-11-02T10:30:00Z", resolvedAt: null, resolution: null, note: null, ...o,
});
type Call = { url: string; init?: RequestInit };
function stub(handler: (c: Call) => Response) {
  const calls: Call[] = [];
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => { calls.push({ url, init }); return handler({ url, init }); }));
  return calls;
}
const booking = (priceMinor: number) => Response.json({ id: "demo-b1", price: { amountMinor: priceMinor, currency: "PLN" } });
const NOTE = "Demo: decyzja moderatora po analizie";

async function type(label: string, value: string) {
  const l = [...host.querySelectorAll("label")].find((x) => x.textContent?.includes(label));
  const el = l?.htmlFor ? host.querySelector<HTMLElement>("#" + CSS.escape(l.htmlFor)) : null;
  if (!el) throw new Error("no field " + label);
  await userEvent.type(el, value);
}
const openPanel = async (price = 10000) => {
  stub((c) => (c.url.includes("/bookings/demo-b1") && !c.init?.method ? booking(price) : Response.json({})));
  render(<DisputeList items={[dispute()]} mode="open" />);
  await act(async () => button(new RegExp(dv("resolve.open"), "i"))!.click());
  await flush();
};

describe("what a row shows", () => {
  it("kind in Polish, Warsaw time, party ids, and the text as plain text", () => {
    render(<DisputeList items={[dispute()]} mode="open" />);
    expect(host.textContent).toContain(dv("kind.not_as_described"));
    expect(host.textContent).toContain("demo-client");
    expect(host.textContent).toContain("demo-practitioner");
    expect(host.querySelector("time")?.getAttribute("datetime")).toBe("2026-11-02T10:30:00Z");
    expect(host.querySelector("time")?.textContent).toContain("11:30"); // 10:30 UTC is 11:30 in Warsaw in November
    expect(host.textContent).toContain("<b>konsultacja</b>"); // escaped, not injected
    expect(host.querySelector("b")).toBeNull();
  });
  it("an empty list says so", () => {
    render(<DisputeList items={[]} mode="open" />);
    expect(host.textContent).toContain(dv("empty.open"));
  });
  it("a resolved dispute shows the outcome and the note and has no resolve panel", () => {
    render(<DisputeList items={[dispute({ resolution: "refund_full", resolvedAt: "2026-11-03T08:00:00Z", note: NOTE })]} mode="resolved" />);
    expect(host.textContent).toContain(dv("resolution.refund_full"));
    expect(host.textContent).toContain(NOTE);
    expect(button(new RegExp(dv("resolve.open"), "i"))).toBeUndefined();
  });
});

describe("resolve panel", () => {
  it("offers the three outcomes", async () => {
    await openPanel();
    const radios = [...host.querySelectorAll<HTMLInputElement>('input[type="radio"]')].map((r) => r.value);
    expect(radios).toEqual(["release", "refund_full", "refund_partial"]);
  });
  it("the amount field exists only for the partial refund", async () => {
    await openPanel();
    expect(host.querySelector('input[name="amount"]')).toBeNull();
    await act(async () => host.querySelector<HTMLInputElement>('input[value="refund_partial"]')!.click());
    expect(host.querySelector('input[name="amount"]')).not.toBeNull();
  });
  it("a note under 10 characters blocks the confirmation step and nothing is sent", async () => {
    const calls = stub(() => booking(10000));
    render(<DisputeList items={[dispute()]} mode="open" />);
    await act(async () => button(new RegExp(dv("resolve.open"), "i"))!.click());
    await flush();
    await act(async () => host.querySelector<HTMLInputElement>('input[value="release"]')!.click());
    await type(dv("resolve.note"), "krótka");
    await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
    expect(host.textContent).toContain(dv("resolve.noteInvalid"));
    expect(button(new RegExp(dv("resolve.confirm"), "i"))).toBeUndefined();
    expect(calls.filter((c) => c.init?.method === "POST")).toHaveLength(0);
  });
  it("partial: 12,50 goes as 1250 grosz after an explicit confirmation that states the money consequence", async () => {
    await openPanel(10000);
    const calls = (globalThis.fetch as unknown as { mock: { calls: [string, RequestInit?][] } }).mock.calls;
    await act(async () => host.querySelector<HTMLInputElement>('input[value="refund_partial"]')!.click());
    await userEvent.type(host.querySelector<HTMLInputElement>('input[name="amount"]')!, "12,50");
    await type(dv("resolve.note"), NOTE);
    await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
    expect(calls.filter(([, i]) => i?.method === "POST")).toHaveLength(0); // not yet
    expect(host.textContent).toContain("12,50"); // the confirmation names the amount
    expect(host.textContent).toContain(dv("confirm.partial", { refund: formatPln(1250), kept: formatPln(8750) }));
    await act(async () => button(new RegExp(dv("resolve.confirm"), "i"))!.click());
    await flush();
    const post = calls.find(([, i]) => i?.method === "POST")!;
    expect(post[0]).toBe("/api/proxy/booking/api/v1/admin/bookings/demo-b1/resolve");
    expect(JSON.parse(String(post[1]!.body))).toEqual({ resolution: "refund_partial", refundMinor: 1250, note: NOTE });
    expect(refresh).toHaveBeenCalled(); // the list is reloaded from the server
  });
  it("release and refund_full send no refundMinor and state who gets the money", async () => {
    for (const [value, key] of [["release", "confirm.release"], ["refund_full", "confirm.refund_full"]] as const) {
      await openPanel(10000);
      await act(async () => host.querySelector<HTMLInputElement>(`input[value="${value}"]`)!.click());
      await type(dv("resolve.note"), NOTE);
      await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
      expect(host.textContent).toContain(dv(key, { price: formatPln(10000) }));
      await act(async () => button(new RegExp(dv("resolve.confirm"), "i"))!.click());
      await flush();
      const calls = (globalThis.fetch as unknown as { mock: { calls: [string, RequestInit?][] } }).mock.calls;
      const body = JSON.parse(String(calls.filter(([, i]) => i?.method === "POST").at(-1)![1]!.body));
      expect(body).toEqual({ resolution: value, note: NOTE });
      act(() => root.unmount());
      host.remove();
      host = document.createElement("div");
      document.body.appendChild(host);
      root = createRoot(host);
    }
  });
  it("partial: more than two decimals, zero and the whole price are rejected before anything is sent", async () => {
    for (const bad of ["12,505", "0", "100,00", "150"]) {
      await openPanel(10000);
      const calls = (globalThis.fetch as unknown as { mock: { calls: [string, RequestInit?][] } }).mock.calls;
      await act(async () => host.querySelector<HTMLInputElement>('input[value="refund_partial"]')!.click());
      await userEvent.type(host.querySelector<HTMLInputElement>('input[name="amount"]')!, bad);
      await type(dv("resolve.note"), NOTE);
      await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
      expect(button(new RegExp(dv("resolve.confirm"), "i")), bad).toBeUndefined();
      expect(calls.filter(([, i]) => i?.method === "POST")).toHaveLength(0);
      act(() => root.unmount());
      host.remove();
      host = document.createElement("div");
      document.body.appendChild(host);
      root = createRoot(host);
    }
  });
  it("409 shows 'state changed' and reloads", async () => {
    stub((c) => (c.init?.method === "POST" ? Response.json({ code: "invalid_transition" }, { status: 409 }) : booking(10000)));
    render(<DisputeList items={[dispute()]} mode="open" />);
    await act(async () => button(new RegExp(dv("resolve.open"), "i"))!.click());
    await flush();
    await act(async () => host.querySelector<HTMLInputElement>('input[value="release"]')!.click());
    await type(dv("resolve.note"), NOTE);
    await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
    await act(async () => button(new RegExp(dv("resolve.confirm"), "i"))!.click());
    await flush();
    expect(host.textContent).toContain(dv("error.stateChanged"));
    expect(refresh).toHaveBeenCalled();
  });
  it("when the price cannot be read the server decides: its 400 on refundMinor is shown in Polish", async () => {
    stub((c) =>
      c.init?.method === "POST"
        ? Response.json({ code: "validation_failed", errors: { refundMinor: ["The refund is 1 to 9999 grosz."] } }, { status: 400 })
        : new Response(null, { status: 500 }));
    render(<DisputeList items={[dispute()]} mode="open" />);
    await act(async () => button(new RegExp(dv("resolve.open"), "i"))!.click());
    await flush();
    await act(async () => host.querySelector<HTMLInputElement>('input[value="refund_partial"]')!.click());
    await userEvent.type(host.querySelector<HTMLInputElement>('input[name="amount"]')!, "500");
    await type(dv("resolve.note"), NOTE);
    await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
    await act(async () => button(new RegExp(dv("resolve.confirm"), "i"))!.click());
    await flush();
    expect(host.textContent).toContain(dv("error.refundMinor"));
    expect(refresh).not.toHaveBeenCalled();
  });
  it("never logs the dispute text or the note", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((m) => vi.spyOn(console, m).mockImplementation(() => {}));
    stub((c) => (c.init?.method === "POST" ? new Response(null, { status: 500 }) : booking(10000)));
    render(<DisputeList items={[dispute()]} mode="open" />);
    await act(async () => button(new RegExp(dv("resolve.open"), "i"))!.click());
    await flush();
    await act(async () => host.querySelector<HTMLInputElement>('input[value="release"]')!.click());
    await type(dv("resolve.note"), NOTE);
    await act(async () => button(new RegExp(dv("resolve.review"), "i"))!.click());
    await act(async () => button(new RegExp(dv("resolve.confirm"), "i"))!.click());
    await flush();
    for (const s of spies) expect(JSON.stringify(s.mock.calls)).not.toMatch(/konsultacja|decyzja moderatora/);
  });
});
