// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { SlotPicker } from "./SlotPicker";
import { ss } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const NOW = new Date("2027-03-26T10:00:00Z");
const PID = "00000000-0000-0000-0000-000000000001";
const OID = "00000000-0000-0000-0000-0000000000a1";
let host: HTMLElement;
let root: Root;
beforeEach(() => {
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});
const mount = async () => {
  await act(async () => root.render(<SlotPicker practitionerId={PID} offeringId={OID} now={NOW} />));
  await act(async () => {}); // let the effect's fetch settle
};
const stub = (impl: (url: string) => Response | Promise<Response>) => {
  const calls: string[] = [];
  vi.stubGlobal("fetch", async (url: string) => {
    calls.push(url);
    return impl(url);
  });
  return calls;
};
const slots = (...starts: string[]) => Response.json({ slots: starts.map((s) => ({ startUtc: s, endUtc: s })) });

describe("slot picker", () => {
  it("shows Warsaw local times for UTC instants across the DST change, as booking links", async () => {
    stub(() => slots("2027-03-27T23:30:00Z", "2027-03-28T01:30:00Z"));
    await mount();
    const links = [...host.querySelectorAll("a")];
    expect(links.map((a) => a.textContent)).toEqual(["00:30", "03:30"]);
    expect(links[1].getAttribute("href")).toBe(`/rezerwacja/${OID}?start=${encodeURIComponent("2027-03-28T01:30:00Z")}`);
    expect(host.textContent).toContain("28 marca");
  });

  it("reads slots through the BFF only, with the offering and a window of at most 14 days", async () => {
    const calls = stub(() => slots());
    await mount();
    const url = new URL(calls[0], "http://x.test");
    expect(url.pathname).toBe(`/api/proxy/booking/api/v1/practitioners/${PID}/slots`);
    expect(calls[0].startsWith("/api/proxy/booking/")).toBe(true);
    expect(url.searchParams.get("offeringId")).toBe(OID);
    expect(url.searchParams.get("from")).toBe("2027-03-26");
    const days = (Date.parse(url.searchParams.get("to")!) - Date.parse(url.searchParams.get("from")!)) / 86_400_000 + 1;
    expect(days).toBeLessThanOrEqual(14);
  });

  it("offers no slot after the 60 day horizon even if the API returns one", async () => {
    stub(() => slots("2027-03-27T09:00:00Z", "2027-06-30T09:00:00Z"));
    await mount();
    expect([...host.querySelectorAll("a")].map((a) => a.textContent)).toEqual(["10:00"]);
  });

  it("shows a clear empty state when there are no slots at all", async () => {
    stub(() => slots());
    await act(async () => root.render(<SlotPicker practitionerId={PID} offeringId={OID} now={new Date("2027-05-20T10:00:00Z")} />));
    await act(async () => {});
    await act(async () => {});
    expect(host.textContent).toContain(ss("slots.empty"));
    expect(host.querySelectorAll("a").length).toBe(0);
  });

  it("loads the next window on request", async () => {
    const calls = stub((u) => (u.includes("from=2027-03-26") ? slots("2027-03-27T09:00:00Z") : slots("2027-04-12T08:00:00Z")));
    await mount();
    const more = [...host.querySelectorAll("button")].find((b) => b.textContent === ss("slots.more"))!;
    await act(async () => more.click());
    expect(calls.length).toBe(2);
    expect([...host.querySelectorAll("a")].map((a) => a.textContent)).toEqual(["10:00", "10:00"]);
  });

  it.each([
    ["a 503", () => new Response("{}", { status: 503 })],
    ["a 500", () => new Response("{}", { status: 500 })],
    ["a network error", () => Promise.reject(new TypeError("down"))],
    ["a malformed body", () => new Response("not json", { status: 200 })],
  ])("Booking down (%s) shows a message and no slots", async (_n, impl) => {
    stub(impl as () => Response);
    await mount();
    expect(host.textContent).toContain(ss("slots.unavailable"));
    expect(host.querySelectorAll("a").length).toBe(0);
  });
});
