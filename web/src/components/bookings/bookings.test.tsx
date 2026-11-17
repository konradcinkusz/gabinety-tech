// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BookingDetail } from "./BookingDetail";
import { MyBookings } from "./MyBookings";
import { mb } from "./strings";
import { reviewHref } from "./routes";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const view = (o: Record<string, unknown> = {}) => ({
  id: "demo-b1", status: "Confirmed", startUtc: "2027-03-28T11:00:00Z", endUtc: "2027-03-28T12:00:00Z", durationMinutes: 60, offeringTitle: "Demo konsultacja",
  price: { amountMinor: 25000, currency: "PLN" }, practitioner: { id: "demo-p1", displayName: "", slug: "" }, client: { displayName: "" },
  brief: { text: "Demo opis problemu z wdrożeniem", aiAssisted: false }, payment: { status: "Succeeded", refunded: { amountMinor: 0, currency: "PLN" } },
  canCancel: false, canDispute: false, canReportNoShow: false, canReview: false, ...o,
});
let host: HTMLElement;
let root: Root;
let calls: { url: string; init: RequestInit }[];
beforeEach(() => {
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
  calls = [];
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
type Handler = (url: string, init: RequestInit) => Response;
const stub = (h: Handler) => vi.stubGlobal("fetch", async (url: string, init: RequestInit = {}) => { calls.push({ url, init }); return h(url, init); });
const settle = () => act(async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); });
const mount = async (el: React.ReactElement) => { await act(async () => root.render(el)); await settle(); };
const buttons = () => [...host.querySelectorAll("button")] as HTMLButtonElement[];
const button = (text: string) => buttons().find((b) => b.textContent?.includes(text));
const click = async (el: Element | undefined) => { expect(el).toBeTruthy(); await act(async () => { (el as HTMLElement).click(); }); await settle(); };
const type = async (el: HTMLTextAreaElement | HTMLSelectElement, value: string) => {
  const proto = el instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLTextAreaElement.prototype;
  await act(async () => { Object.getOwnPropertyDescriptor(proto, "value")!.set!.call(el, value); el.dispatchEvent(new Event(el instanceof HTMLSelectElement ? "change" : "input", { bubbles: true })); });
};
const detail = (b: Record<string, unknown>, role: "client" | "practitioner" = "client") => {
  stub((url, init) => (init.method === "GET" || !init.method ? Response.json(view(b)) : Response.json(view({ ...b, status: "CancelledByClient", canCancel: false }))));
  return mount(<BookingDetail bookingId="demo-b1" role={role} />);
};
const ACTIONS = [mb("actions.cancel"), mb("actions.dispute"), mb("actions.noShow")];

describe("booking detail: actions follow the server flags", () => {
  it("shows no action and no review link when every flag is false", async () => {
    await detail({});
    for (const a of ACTIONS) expect(button(a)).toBeUndefined();
    expect(host.querySelector(`a[href="${reviewHref("demo-b1")}"]`)).toBeNull();
  });

  it("shows exactly the actions whose flag is true", async () => {
    await detail({ canDispute: true });
    expect(button(mb("actions.dispute"))).toBeTruthy();
    expect(button(mb("actions.cancel"))).toBeUndefined();
    expect(button(mb("actions.noShow"))).toBeUndefined();
  });

  it("renders the review link only for canReview, from the one route constant", async () => {
    await detail({ canReview: true });
    expect(host.querySelector(`a[href="${reviewHref("demo-b1")}"]`)).toBeTruthy();
  });
});

describe("booking detail: meeting link and degraded names", () => {
  it("has no meeting link for PendingPayment (the API sends none)", async () => {
    await detail({ status: "PendingPayment", meetingUrl: undefined });
    expect(host.textContent).not.toContain(mb("detail.meeting"));
    expect(host.querySelector("a[target=_blank]")).toBeNull();
  });

  it("shows the meeting link when the API returns it, and refuses a non-http scheme", async () => {
    await detail({ meetingUrl: "https://meet.example.test/demo" });
    expect(host.querySelector<HTMLAnchorElement>("a[target=_blank]")?.href).toBe("https://meet.example.test/demo");
    act(() => root.unmount());
    root = createRoot(host);
    await detail({ meetingUrl: "javascript:alert(1)" });
    expect(host.querySelector("a[target=_blank]")).toBeNull();
  });

  it("renders without party names, with the offering title and a neutral label, calling only the booking API", async () => {
    await detail({});
    expect(host.textContent).toContain("Demo konsultacja");
    expect(host.textContent).toContain(mb("party.practitioner"));
    expect(calls.every((c) => c.url.startsWith("/api/proxy/booking/"))).toBe(true);
  });

  it("shows the times in Europe/Warsaw and the payment state", async () => {
    await detail({ startUtc: "2027-03-28T11:00:00Z", endUtc: "2027-03-28T12:00:00Z" });
    expect(host.textContent).toContain("13:00"); // CEST after the 28 March change
    expect(host.textContent).toContain(mb("payment.Succeeded"));
  });

  it("shows the brief and uses text, not markup", async () => {
    await detail({ brief: { text: "<script>x</script> demo", aiAssisted: false } });
    expect(host.textContent).toContain("<script>x</script> demo");
    expect(host.querySelector("script")).toBeNull();
  });
});

describe("cancel", () => {
  it("tells the client the refund rule before confirming and sends nothing until then", async () => {
    await detail({ canCancel: true });
    await click(button(mb("actions.cancel")));
    expect(host.textContent).toContain(mb("cancel.clientRule"));
    expect(host.textContent).toMatch(/zwrot/);
    expect(host.textContent).toMatch(/brak zwrotu/);
    expect(calls.filter((c) => c.init.method === "POST")).toHaveLength(0);
  });

  it("tells the practitioner the refund is always full, with no no-refund branch", async () => {
    await detail({ canCancel: true }, "practitioner");
    await click(button(mb("actions.cancel")));
    expect(host.textContent).toContain(mb("cancel.practitionerRule"));
    expect(host.textContent).not.toMatch(/brak zwrotu/);
  });

  it("posts the reason on confirm and shows the new state", async () => {
    await detail({ canCancel: true });
    await click(button(mb("actions.cancel")));
    await type(host.querySelector("textarea")!, "Demo powód");
    await click(button(mb("cancel.confirm")));
    const post = calls.find((c) => c.init.method === "POST")!;
    expect(post.url).toBe("/api/proxy/booking/api/v1/bookings/demo-b1/cancel");
    expect(JSON.parse(post.init.body as string)).toEqual({ reason: "Demo powód" });
    expect(host.textContent).toContain(mb("status.CancelledByClient"));
    expect(button(mb("actions.cancel"))).toBeUndefined();
  });

  it("on invalid_transition says it is no longer possible and offers a refresh", async () => {
    stub((_u, init) => (init.method === "POST" ? Response.json({ code: "invalid_transition" }, { status: 409 }) : Response.json(view({ canCancel: true }))));
    await mount(<BookingDetail bookingId="demo-b1" role="client" />);
    await click(button(mb("actions.cancel")));
    await click(button(mb("cancel.confirm")));
    expect(host.textContent).toContain(mb("error.invalidTransition"));
    expect(button(mb("error.refresh"))).toBeTruthy();
  });
});

describe("dispute", () => {
  const open = async () => {
    await detail({ canDispute: true });
    await click(button(mb("actions.dispute")));
  };
  it("rejects 9 characters without calling the server and accepts 10", async () => {
    await open();
    await type(host.querySelector("textarea")!, "123456789");
    await click(button(mb("dispute.submit")));
    expect(host.textContent).toContain(mb("dispute.tooShort"));
    expect(calls.filter((c) => c.init.method === "POST")).toHaveLength(0);
    await type(host.querySelector("textarea")!, "1234567890");
    await click(button(mb("dispute.submit")));
    const post = calls.find((c) => c.init.method === "POST")!;
    expect(post.url).toBe("/api/proxy/booking/api/v1/bookings/demo-b1/dispute");
    expect(JSON.parse(post.init.body as string)).toEqual({ kind: "not_as_described", text: "1234567890" });
  });

  it("sends the chosen kind in its wire form and rejects over 1000 characters", async () => {
    await open();
    await type(host.querySelector("select")!, "practitioner_no_show");
    await type(host.querySelector("textarea")!, "x".repeat(1001));
    await click(button(mb("dispute.submit")));
    expect(host.textContent).toContain(mb("dispute.tooLong"));
    await type(host.querySelector("textarea")!, "x".repeat(1000));
    await click(button(mb("dispute.submit")));
    expect(JSON.parse(calls.find((c) => c.init.method === "POST")!.init.body as string).kind).toBe("practitioner_no_show");
  });

  it("shows the server's field errors on validation_failed", async () => {
    stub((_u, init) => (init.method === "POST" ? Response.json({ code: "validation_failed", errors: { text: ["too short"] } }, { status: 400 }) : Response.json(view({ canDispute: true }))));
    await mount(<BookingDetail bookingId="demo-b1" role="client" />);
    await click(button(mb("actions.dispute")));
    await type(host.querySelector("textarea")!, "1234567890");
    await click(button(mb("dispute.submit")));
    expect(host.textContent).toContain(mb("dispute.tooShort"));
  });
});

describe("report client no-show", () => {
  it("is practitioner-only through its flag and posts without a body", async () => {
    await detail({ canReportNoShow: true }, "practitioner");
    await click(button(mb("actions.noShow")));
    await click(button(mb("noShow.confirm")));
    const post = calls.find((c) => c.init.method === "POST")!;
    expect(post.url).toBe("/api/proxy/booking/api/v1/bookings/demo-b1/report-client-no-show");
    expect(post.init.body).toBeUndefined();
  });
});

describe("errors by code", () => {
  const load = async (status: number, body: Record<string, unknown>) => {
    stub(() => Response.json(body, { status }));
    await mount(<BookingDetail bookingId="demo-b1" role="client" />);
  };
  it("404", async () => { await load(404, { code: "not_found" }); expect(host.textContent).toContain(mb("error.notFound")); });
  it("503", async () => { await load(503, { code: "dependency_unavailable" }); expect(host.textContent).toContain(mb("error.unavailable")); });
  it("never writes to the console, even with free text in flight", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((m) => vi.spyOn(console, m).mockImplementation(() => {}));
    stub((_u, init) => (init.method === "POST" ? Response.json({ code: "boom" }, { status: 500 }) : Response.json(view({ canDispute: true }))));
    await mount(<BookingDetail bookingId="demo-b1" role="client" />);
    await click(button(mb("actions.dispute")));
    await type(host.querySelector("textarea")!, "tajny opis sporu demo");
    await click(button(mb("dispute.submit")));
    expect(spies.flatMap((s) => s.mock.calls)).toEqual([]);
  });
});

describe("my bookings list", () => {
  const page = (n: number, next?: string) => ({ items: Array.from({ length: n }, (_, i) => view({ id: `demo-${next ?? "last"}-${i}` })), ...(next ? { nextCursor: next } : {}) });
  it("asks for the client role first, switches role and filters by status, resetting the cursor", async () => {
    stub(() => Response.json(page(2)));
    await mount(<MyBookings />);
    expect(calls[0].url).toBe("/api/proxy/booking/api/v1/me/bookings?role=client");
    await click(button(mb("role.practitioner")));
    expect(calls.at(-1)!.url).toBe("/api/proxy/booking/api/v1/me/bookings?role=practitioner");
    await type(host.querySelector("select")!, "Confirmed");
    expect(calls.at(-1)!.url).toBe("/api/proxy/booking/api/v1/me/bookings?role=practitioner&status=Confirmed");
  });

  it("pages by cursor, appending, and hides the button on the last page", async () => {
    stub((url) => Response.json(url.includes("cursor=c1") ? page(1) : page(2, "c1")));
    await mount(<MyBookings />);
    expect(host.querySelectorAll("li")).toHaveLength(2);
    await click(button(mb("list.more")));
    expect(calls.at(-1)!.url).toBe("/api/proxy/booking/api/v1/me/bookings?role=client&cursor=c1");
    expect(host.querySelectorAll("li")).toHaveLength(3);
    expect(button(mb("list.more"))).toBeUndefined();
  });

  it("links each row to its detail with the role, falls back to the offering title and says when empty", async () => {
    stub(() => Response.json(page(1)));
    await mount(<MyBookings />);
    expect(host.querySelector("li")!.textContent).toContain("Demo konsultacja");
    expect(host.querySelector<HTMLAnchorElement>("li a")!.getAttribute("href")).toBe("/moje/rezerwacje/demo-last-0?role=client");
    act(() => root.unmount());
    root = createRoot(host);
    stub(() => Response.json({ items: [] }));
    await mount(<MyBookings />);
    expect(host.textContent).toContain(mb("list.empty"));
  });

  it("shows the code-based error for a failed list", async () => {
    stub(() => Response.json({ code: "dependency_unavailable" }, { status: 503 }));
    await mount(<MyBookings />);
    expect(host.textContent).toContain(mb("error.unavailable"));
  });
});
