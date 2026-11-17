// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BookingForm } from "./BookingForm";
import { bk } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const offering = { id: "demo-o1", title: "Demo konsultacja", durationMinutes: 60, price: { amountMinor: 25000, currency: "PLN" as const }, problemSlug: "demo-problem" };
const practitioner = { slug: "demo-jan", displayName: "Demo Jan" };
const START = "2027-03-28T10:00:00Z";
const BRIEF = "Demo: moja aplikacja wolno ładuje listę zamówień.";
const created = () => Response.json({ bookingId: "demo-b1", status: "PendingPayment", holdExpiresAt: "2027-03-28T09:10:00Z", checkoutUrl: "http://localhost:3000/checkout/mock/demo-b1" }, { status: 201 });
const suggestion = { problemSlug: "demo-other", techSlugs: [], summary: "Demo: wolne ładowanie listy zamówień.", clarifyingQuestions: ["Demo: jaka jest liczba rekordów?"], suggestedDurationMinutes: 60, confidence: "medium", modelId: "demo", promptVersion: "1" };

let host: HTMLElement;
let root: Root;
let calls: { url: string; init: RequestInit }[];
let keys: number;
beforeEach(() => {
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
  calls = [];
  keys = 0;
});
afterEach(() => {
  act(() => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
const stub = (impl: (url: string, n: number) => Response | Promise<Response>) =>
  vi.stubGlobal("fetch", async (url: string, init: RequestInit = {}) => {
    calls.push({ url, init });
    return impl(url, calls.length);
  });
const navigate = vi.fn();
const mount = () => act(async () => root.render(<BookingForm offering={offering} practitioner={practitioner} startUtc={START} navigate={navigate} newKey={() => `key-${++keys}`} />));
const textarea = () => host.querySelector("textarea") as HTMLTextAreaElement;
const button = (text: string) => [...host.querySelectorAll("button")].find((b) => b.textContent?.includes(text)) as HTMLButtonElement;
const type = async (text: string) => {
  await userEvent.clear(textarea());
  await userEvent.type(textarea(), text);
};
const posts = () => calls.filter((c) => c.url.endsWith("/api/v1/bookings"));
const bodyOf = (c: { init: RequestInit }) => JSON.parse(String(c.init.body));
const keyOf = (c: { init: RequestInit }) => new Headers(c.init.headers).get("idempotency-key");

describe("booking form", () => {
  it("shows the slot in Warsaw time and the price", async () => {
    await mount();
    expect(host.textContent).toContain("12:00"); // 10:00Z is 12:00 in Warsaw (CEST from 28 March 2027)
    expect(host.textContent).toContain("250,00");
  });

  it("sends one request on a double click and disables the button while pending", async () => {
    let release!: (r: Response) => void;
    stub(() => new Promise<Response>((r) => (release = r)));
    await mount();
    await type(BRIEF);
    const pay = button(bk("form.pay"));
    await act(async () => {
      pay.click();
      pay.click();
    });
    expect(posts()).toHaveLength(1);
    expect(button(bk("form.paying")).disabled).toBe(true);
    await act(async () => release(created()));
    expect(navigate).toHaveBeenLastCalledWith("/checkout/mock/demo-b1");
  });

  it("reuses the Idempotency-Key when the same request is retried after a network error", async () => {
    stub((_u, n) => (n === 1 ? Promise.reject(new TypeError("offline")) : created()));
    await mount();
    await type(BRIEF);
    await act(async () => button(bk("form.pay")).click());
    expect(host.textContent).toContain(bk("error.unavailable"));
    await act(async () => button(bk("form.pay")).click());
    expect(posts()).toHaveLength(2);
    expect(keyOf(posts()[0])).toBe("key-1");
    expect(keyOf(posts()[1])).toBe("key-1");
    expect(navigate).toHaveBeenLastCalledWith("/checkout/mock/demo-b1");
  });

  it("takes a new key when the body changed (same key with another body is a 409 idempotency_conflict)", async () => {
    stub((_u, n) => (n === 1 ? new Response("{}", { status: 503 }) : created()));
    await mount();
    await type(BRIEF);
    await act(async () => button(bk("form.pay")).click());
    await userEvent.type(textarea(), " Dodatkowy opis problemu.");
    await act(async () => button(bk("form.pay")).click());
    expect(keyOf(posts()[0])).toBe("key-1");
    expect(keyOf(posts()[1])).toBe("key-2");
  });

  it("does not send a brief shorter than 20 characters after trimming, and sends the trimmed text otherwise", async () => {
    stub(() => created());
    await mount();
    await type("   " + "a".repeat(19) + "   ");
    await act(async () => button(bk("form.pay")).click());
    expect(posts()).toHaveLength(0);
    expect(host.textContent).toContain(bk("brief.tooShort"));
    await type("  " + BRIEF + "  ");
    await act(async () => button(bk("form.pay")).click());
    expect(bodyOf(posts()[0])).toEqual({ offeringId: "demo-o1", startUtc: START, brief: { text: BRIEF, aiAssisted: false } });
    expect(posts()[0].url).toBe("/api/proxy/booking/api/v1/bookings");
  });

  it("slot_taken and slot_unavailable keep the user off the payment and point back to the profile", async () => {
    stub(() => Response.json({ code: "slot_taken" }, { status: 409 }));
    await mount();
    await type(BRIEF);
    await act(async () => button(bk("form.pay")).click());
    expect(host.textContent).toContain(bk("error.slotTaken"));
    expect(host.querySelector(`a[href="/gabinet/demo-jan"]`)).not.toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it("an unauthenticated answer sends the user to sign in and back to this step", async () => {
    stub(() => Response.json({ code: "unauthenticated" }, { status: 401 }));
    await mount();
    await type(BRIEF);
    await act(async () => button(bk("form.pay")).click());
    expect(navigate).toHaveBeenCalledWith("/logowanie?redirect=" + encodeURIComponent(`/rezerwacja/demo-o1?start=${encodeURIComponent(START)}&gabinet=demo-jan`));
  });
});

describe("brief assistant", () => {
  const ask = async () => {
    await type(BRIEF);
    await act(async () => button(bk("assist.ask")).click());
  };

  it("only suggests: the textarea is unchanged until a click, and an unused suggestion leaves aiAssisted false", async () => {
    stub((url) => (url.includes("/assistant/") ? Response.json(suggestion) : created()));
    await mount();
    await ask();
    expect(textarea().value).toBe(BRIEF);
    expect((host.querySelector("[data-testid=proposal]") as HTMLTextAreaElement).value).toBe(suggestion.summary);
    expect(host.textContent).toContain(suggestion.clarifyingQuestions[0]);
    await act(async () => button(bk("form.pay")).click());
    expect(bodyOf(posts()[0]).brief).toEqual({ text: BRIEF, aiAssisted: false });
  });

  it("aiAssisted is true, with the problem slug, only after the edited proposal is applied", async () => {
    stub((url) => (url.includes("/assistant/") ? Response.json(suggestion) : created()));
    await mount();
    await ask();
    await userEvent.type(host.querySelector("[data-testid=proposal]") as HTMLTextAreaElement, " Edytowane.");
    await act(async () => button(bk("assist.apply")).click());
    expect(textarea().value).toContain("Edytowane.");
    expect(textarea().value.startsWith(BRIEF)).toBe(true);
    await act(async () => button(bk("form.pay")).click());
    const brief = bodyOf(posts()[0]).brief;
    expect(brief.aiAssisted).toBe(true);
    expect(brief.problemSlug).toBe("demo-other");
    expect(brief.text).toContain("Edytowane.");
  });

  it.each([503, 429])("with the assistant answering %i the form stays usable and the booking still submits", async (status) => {
    stub((url) => (url.includes("/assistant/") ? Response.json({ code: "x" }, { status }) : created()));
    await mount();
    await ask();
    expect(host.textContent).toContain(bk(status === 503 ? "assist.unavailable" : "assist.rateLimited"));
    await act(async () => button(bk("form.pay")).click());
    expect(bodyOf(posts()[0]).brief.aiAssisted).toBe(false);
    expect(navigate).toHaveBeenLastCalledWith("/checkout/mock/demo-b1");
  });

  it("asks only from 20 characters on and posts only the text to the BFF; the brief is never logged", async () => {
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((m) => vi.spyOn(console, m).mockImplementation(() => {}));
    stub(() => Response.json(suggestion));
    await mount();
    await type("za krótko");
    expect(button(bk("assist.ask")).disabled).toBe(true);
    await ask();
    expect(calls[0].url).toBe("/api/proxy/assistant/api/v1/assistant/brief");
    expect(bodyOf(calls[0])).toEqual({ text: BRIEF });
    expect(spies.flatMap((s) => s.mock.calls.flat().map(String)).join(" ")).not.toContain("zamówień");
  });
});
