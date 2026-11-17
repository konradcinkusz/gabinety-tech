// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ProfileReviews } from "./ProfileReviews";
import { pf } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

const PID = "00000000-0000-0000-0000-000000000001";
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
const review = (n: number, over: Record<string, unknown> = {}) => ({ id: `r${n}`, rating: 5, text: `demo-opinia ${n}`, createdAt: "2027-03-28T01:30:00Z", ...over });
const stub = (impl: (url: string) => Response | Promise<Response>) => {
  const calls: string[] = [];
  vi.stubGlobal("fetch", async (url: string) => {
    calls.push(url);
    return impl(url);
  });
  return calls;
};
const mount = async () => {
  await act(async () => root.render(<ProfileReviews practitionerId={PID} />));
  await act(async () => {});
};

describe("profile reviews list", () => {
  it("reads published reviews through the BFF reputation proxy only", async () => {
    const calls = stub(() => Response.json({ items: [] }));
    await mount();
    const url = new URL(calls[0], "http://x.test");
    expect(url.pathname).toBe(`/api/proxy/reputation/api/v1/practitioners/${PID}/reviews`);
    expect(url.searchParams.get("limit")).toBe("20");
    expect(host.textContent).toContain(pf("reviews.empty"));
  });

  it("shows the anonymous label, the rating, the Warsaw date and the owner reply", async () => {
    stub(() => Response.json({ items: [review(1, { reply: { text: "demo-odpowiedź", repliedAt: "2027-03-29T10:00:00Z" } }), review(2, { rating: 4 })] }));
    await mount();
    const items = [...host.querySelectorAll("li")];
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain(pf("reviews.reviewer"));
    expect(items[0].textContent).toContain("28 marca 2027");
    expect(items[0].textContent).toContain(pf("reviews.replyHeading"));
    expect(items[0].textContent).toContain("demo-odpowiedź");
    expect(items[1].textContent).toContain(pf("reviews.rating", { n: 4 }));
    expect(items[1].textContent).not.toContain(pf("reviews.replyHeading"));
  });

  it("renders review text as text, never as HTML", async () => {
    stub(() => Response.json({ items: [review(1, { text: "<img src=x onerror=alert(1)>" })] }));
    await mount();
    expect(host.querySelector("img")).toBeNull();
    expect(host.textContent).toContain("<img src=x onerror=alert(1)>");
  });

  it("pages with the cursor and appends", async () => {
    const calls = stub((u) => (u.includes("cursor=C1") ? Response.json({ items: [review(2)] }) : Response.json({ items: [review(1)], nextCursor: "C1" })));
    await mount();
    const more = [...host.querySelectorAll("button")].find((b) => b.textContent === pf("reviews.more"))!;
    await act(async () => more.click());
    await act(async () => {});
    expect(new URL(calls[1], "http://x.test").searchParams.get("cursor")).toBe("C1");
    expect(host.querySelectorAll("li")).toHaveLength(2);
    expect([...host.querySelectorAll("button")].some((b) => b.textContent === pf("reviews.more"))).toBe(false);
  });

  it("an unavailable Reputation shows a message and does not break the page", async () => {
    stub(() => new Response("{}", { status: 503 }));
    await mount();
    expect(host.textContent).toContain(pf("reviews.unavailable"));
  });

  it("offers no reply form and never redirects to sign-in on this public page", async () => {
    stub(() => Response.json({ items: [review(1)] }));
    await mount();
    expect(host.querySelector("textarea")).toBeNull();
  });
});
