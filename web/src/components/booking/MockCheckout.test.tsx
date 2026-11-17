// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Confirmation } from "./Confirmation";
import { MockCheckout } from "./MockCheckout";
import { bk } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const T0 = Date.parse("2027-03-28T09:00:00Z");
const view = (o: Record<string, unknown> = {}) => ({
  id: "demo-b1", status: "PendingPayment", startUtc: "2027-03-28T11:00:00Z", endUtc: "2027-03-28T12:00:00Z", durationMinutes: 60, offeringTitle: "Demo konsultacja",
  price: { amountMinor: 25000, currency: "PLN" }, practitioner: { id: "demo-p1", displayName: "Demo Jan", slug: "demo-jan" }, holdExpiresAt: new Date(T0 + 90_000).toISOString(), ...o,
});
let host: HTMLElement;
let root: Root;
let calls: { url: string; init: RequestInit }[];
beforeEach(() => {
  vi.useFakeTimers({ toFake: ["setInterval", "clearInterval", "Date"], now: T0 });
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
  calls = [];
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
  vi.useRealTimers();
  vi.unstubAllGlobals();
});
const stub = (get: () => Response, simulate: () => Response | Promise<Response> = () => new Response(null, { status: 200 })) =>
  vi.stubGlobal("fetch", async (url: string, init: RequestInit = {}) => {
    calls.push({ url, init });
    return url.includes("/payments/simulate") ? simulate() : get();
  });
const navigate = vi.fn();
const button = (text: string) => [...host.querySelectorAll("button")].find((b) => b.textContent?.includes(text)) as HTMLButtonElement;
const settle = () => act(async () => { await Promise.resolve(); await Promise.resolve(); });
const mount = async (el: React.ReactElement) => { await act(async () => root.render(el)); await settle(); };

describe("mock checkout", () => {
  it("counts the hold down and offers both outcomes", async () => {
    stub(() => Response.json(view()));
    await mount(<MockCheckout bookingId="demo-b1" navigate={navigate} />);
    expect(host.textContent).toContain("1:30");
    await act(async () => { vi.advanceTimersByTime(30_000); });
    expect(host.textContent).toContain("1:00");
    expect(calls[0].url).toBe("/api/proxy/booking/api/v1/bookings/demo-b1");
    expect(button(bk("checkout.succeed"))).toBeTruthy();
    expect(button(bk("checkout.fail"))).toBeTruthy();
  });

  it("on expiry says so, disables both buttons and links back to the profile", async () => {
    stub(() => Response.json(view()));
    await mount(<MockCheckout bookingId="demo-b1" navigate={navigate} />);
    await act(async () => { vi.advanceTimersByTime(91_000); });
    expect(host.textContent).toContain(bk("checkout.expired"));
    expect(button(bk("checkout.succeed")).disabled).toBe(true);
    expect(button(bk("checkout.fail")).disabled).toBe(true);
    expect(host.querySelector(`a[href="/gabinet/demo-jan"]`)).not.toBeNull();
  });

  it("simulates success once on a double click and goes to the confirmation", async () => {
    let release!: (r: Response) => void;
    stub(() => Response.json(view()), () => new Promise<Response>((r) => (release = r)));
    await mount(<MockCheckout bookingId="demo-b1" navigate={navigate} />);
    await act(async () => { button(bk("checkout.succeed")).click(); button(bk("checkout.succeed")).click(); });
    const sims = calls.filter((c) => c.url.includes("/payments/simulate"));
    expect(sims).toHaveLength(1);
    expect(sims[0].url).toBe("/api/proxy/booking/api/v1/payments/simulate");
    expect(JSON.parse(String(sims[0].init.body))).toEqual({ bookingId: "demo-b1", outcome: "succeeded" });
    await act(async () => release(new Response(null, { status: 200 })));
    expect(navigate).toHaveBeenCalledWith("/rezerwacja/potwierdzenie/demo-b1");
  });

  it("a failed payment shows the failure and the way back", async () => {
    stub(() => Response.json(view()));
    await mount(<MockCheckout bookingId="demo-b1" navigate={navigate} />);
    await act(async () => button(bk("checkout.fail")).click());
    expect(JSON.parse(String(calls.at(-1)!.init.body)).outcome).toBe("failed");
    expect(host.textContent).toContain(bk("checkout.failed"));
    expect(navigate).not.toHaveBeenCalled();
  });

  it("outside Mock mode the simulate endpoint answers 403: a clear message, nothing else", async () => {
    stub(() => Response.json(view()), () => Response.json({ code: "forbidden" }, { status: 403 }));
    await mount(<MockCheckout bookingId="demo-b1" navigate={navigate} />);
    await act(async () => button(bk("checkout.succeed")).click());
    expect(host.textContent).toContain(bk("checkout.notMock"));
    expect(navigate).not.toHaveBeenCalled();
  });
});

describe("confirmation", () => {
  it("shows the meeting link only when the booking carries one", async () => {
    stub(() => Response.json(view({ status: "Confirmed", meetingUrl: "https://meet.example.test/demo", holdExpiresAt: undefined })));
    await mount(<Confirmation bookingId="demo-b1" />);
    expect(host.querySelector(`a[href="https://meet.example.test/demo"]`)).not.toBeNull();
    expect(host.textContent).toContain("Demo konsultacja");
  });

  it("does not claim success while the payment is still pending, and never renders a non-http link", async () => {
    stub(() => Response.json(view({ meetingUrl: "javascript:alert(1)" })));
    await mount(<Confirmation bookingId="demo-b1" />);
    expect(host.textContent).toContain(bk("confirmation.pending"));
    expect(host.querySelector("a[href^=javascript]")).toBeNull();
  });
});
