// @vitest-environment jsdom
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { REPLY_ENABLED } from "./lib";
import { PractitionerReviews } from "./PractitionerReviews";
import { ReplyForm } from "./ReplyForm";
import { ReviewForm } from "./ReviewForm";
import { mr } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

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
const settle = () => act(async () => { for (let i = 0; i < 6; i++) await Promise.resolve(); });
const mount = async (el: React.ReactElement) => { await act(async () => root.render(el)); await settle(); };
const buttons = () => [...host.querySelectorAll("button")] as HTMLButtonElement[];
const button = (text: string) => buttons().find((b) => b.textContent?.includes(text));
const click = async (el: Element | undefined | null) => { expect(el).toBeTruthy(); await act(async () => { (el as HTMLElement).click(); }); await settle(); };
const type = async (el: HTMLTextAreaElement, value: string) => {
  await act(async () => { Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")!.set!.call(el, value); el.dispatchEvent(new Event("input", { bubbles: true })); });
};
const area = () => host.querySelector("textarea") as HTMLTextAreaElement;
const star = (n: number) => host.querySelector<HTMLInputElement>(`input[type=radio][value="${n}"]`);
const problem = (status: number, code: string) => Response.json({ code, status }, { status });
const GOOD = "Demo: rzeczowa pomoc, problem rozwiązany w godzinę.";

async function fill(rating = 5, text = GOOD) {
  await click(star(rating));
  await type(area(), text);
}
const submit = () => click(button(mr("form.submit")));
const posts = () => calls.filter((c) => c.init.method === "POST");

describe("review form: validation", () => {
  it("rejects 19 characters without calling the API, accepts 20", async () => {
    stub(() => Response.json({ reviewId: "demo-r1", status: "Published" }, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill(4, "x".repeat(19));
    await submit();
    expect(posts()).toHaveLength(0);
    expect(host.textContent).toContain(mr("form.textTooShort"));
    await type(area(), "x".repeat(20));
    await submit();
    expect(posts()).toHaveLength(1);
  });

  it("needs a rating", async () => {
    stub(() => Response.json({}, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await type(area(), GOOD);
    await submit();
    expect(posts()).toHaveLength(0);
    expect(host.textContent).toContain(mr("form.ratingRequired"));
  });

  it("shows a counter and caps the text at 2000", async () => {
    stub(() => Response.json({}));
    await mount(<ReviewForm bookingId="demo-b1" />);
    expect(area().maxLength).toBe(2000);
    await type(area(), "abc");
    expect(host.textContent).toContain("3 / 2000");
  });

  it("sends the bookingId, rating and trimmed text to the BFF reputation route", async () => {
    stub(() => Response.json({ reviewId: "demo-r1", status: "Published" }, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill(3, `  ${GOOD}  `);
    await submit();
    expect(calls[0].url).toBe("/api/proxy/reputation/api/v1/reviews");
    expect(JSON.parse(String(calls[0].init.body))).toEqual({ bookingId: "demo-b1", rating: 3, text: GOOD });
  });
});

describe("review form: honest confirmation", () => {
  it("says the review is held when the API returns Held, and does not promise publication", async () => {
    stub(() => Response.json({ reviewId: "demo-r1", status: "Held" }, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain("Opinia czeka na sprawdzenie");
    expect(host.textContent).toContain(mr("done.held"));
    expect(host.textContent).not.toContain(mr("done.published"));
    expect(host.querySelector("form")).toBeNull();
  });

  it("says it is published only when the API returns Published", async () => {
    stub(() => Response.json({ reviewId: "demo-r1", status: "Published" }, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("done.published"));
    expect(host.textContent).not.toContain(mr("done.held"));
  });

  it("never echoes the typed text back in the confirmation", async () => {
    stub(() => Response.json({ reviewId: "demo-r1", status: "Held" }, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill(5, "Demo SEKRETNY tekst opinii do sprawdzenia");
    await submit();
    expect(host.textContent).not.toContain("SEKRETNY");
  });

  it("treats an unknown status as held (never promises publication)", async () => {
    stub(() => Response.json({ reviewId: "demo-r1", status: "Whatever" }, { status: 201 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("done.held"));
    expect(host.textContent).not.toContain(mr("done.published"));
  });
});

describe("review form: server refusals in Polish", () => {
  it("a second attempt (409 already_reviewed) shows the already-reviewed state and no form", async () => {
    stub(() => problem(409, "already_reviewed"));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("already.title"));
    expect(host.querySelector("form")).toBeNull();
  });

  it("422 not_reviewable shows its own state", async () => {
    stub(() => problem(422, "not_reviewable"));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("notReviewable.title"));
    expect(host.querySelector("form")).toBeNull();
  });

  it("404 says the booking was not found", async () => {
    stub(() => problem(404, "not_found"));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("error.notFound"));
  });

  it("422 validation_failed keeps the form and the text and shows the text error", async () => {
    stub(() => Response.json({ code: "validation_failed", errors: { text: ["too_short_after_trim"] } }, { status: 422 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("form.textInvalid"));
    expect(area().value).toBe(GOOD);
  });

  it("never shows the server's English title or detail", async () => {
    stub(() => Response.json({ code: "weird", title: "Some English Title", detail: "English detail" }, { status: 500 }));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).not.toContain("English");
    expect(host.textContent).toContain(mr("error.generic"));
  });

  it("503 and a network failure keep the form so the user can retry", async () => {
    stub(() => problem(503, "dependency_unavailable"));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(host.textContent).toContain(mr("error.unavailable"));
    expect(area().value).toBe(GOOD);
    vi.stubGlobal("fetch", async () => { throw new TypeError("offline"); });
    await submit();
    expect(host.textContent).toContain(mr("error.network"));
  });

  it("a 401 sends the user to sign in with returnTo", async () => {
    const assign = vi.fn();
    Object.defineProperty(window, "location", { value: { ...window.location, pathname: "/moje/opinie/demo-b1", search: "", assign }, configurable: true });
    stub(() => problem(401, "unauthorized"));
    await mount(<ReviewForm bookingId="demo-b1" />);
    await fill();
    await submit();
    expect(assign).toHaveBeenCalledWith("/logowanie?returnTo=" + encodeURIComponent("/moje/opinie/demo-b1"));
  });
});

const review = (o: Record<string, unknown> = {}) => ({ id: "demo-r1", rating: 5, text: "Demo tekst opinii", createdAt: "2027-03-01T10:00:00Z", ...o });
const reviewsApi = (items: unknown[], next?: string): Handler => (url, init) => {
  if (url.endsWith("/api/v1/me")) return Response.json({ userId: "u1", practitionerId: "demo-p1" });
  if (url.includes("/reviews") && init.method !== "POST") return Response.json({ items, nextCursor: next });
  return Response.json({}, { status: 201 });
};

describe("practitioner's reviews list: text only", () => {
  it("renders a <script> review as text and creates no element from it", async () => {
    const evil = `<script>window.__pwned=1</script><img src=x onerror="window.__pwned=1">`;
    stub(reviewsApi([review({ text: evil, reply: { text: "<b>odp</b>", repliedAt: "2027-03-02T10:00:00Z" } })]));
    await mount(<PractitionerReviews />);
    expect(host.textContent).toContain(evil);
    expect(host.textContent).toContain("<b>odp</b>");
    expect(host.querySelector("script")).toBeNull();
    expect(host.querySelector("img")).toBeNull();
    expect(host.querySelector("b")).toBeNull();
    expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
  });

  it("reads the practitioner id from /me and lists that practitioner's reviews under the anonymous reviewer label", async () => {
    stub(reviewsApi([review()]));
    await mount(<PractitionerReviews />);
    expect(calls.some((c) => c.url === "/api/proxy/reputation/api/v1/practitioners/demo-p1/reviews?limit=20")).toBe(true);
    expect(host.textContent).toContain(mr("list.reviewer"));
  });

  it("says so when there is no practitioner profile", async () => {
    stub((url) => (url.endsWith("/api/v1/me") ? Response.json({ userId: "u1" }) : Response.json({ items: [] })));
    await mount(<PractitionerReviews />);
    expect(host.textContent).toContain(mr("list.noProfile"));
  });

  it("says so when there is no review", async () => {
    stub(reviewsApi([]));
    await mount(<PractitionerReviews />);
    expect(host.textContent).toContain(mr("list.empty"));
  });

  it("loads the next page with the cursor", async () => {
    stub(reviewsApi([review()], "cur1"));
    await mount(<PractitionerReviews />);
    await click(button(mr("list.more")));
    expect(calls.at(-1)!.url).toContain("cursor=cur1");
  });
});

describe("reply form", () => {
  it("is switched off by one constant (the reply endpoint, T-05.02b, is not on main yet)", () => {
    expect(REPLY_ENABLED).toBe(false);
  });

  it("the list shows no reply form while the constant is off", async () => {
    stub(reviewsApi([review()]));
    await mount(<PractitionerReviews />);
    expect(host.querySelector("textarea")).toBeNull();
  });

  it("when enabled the list offers a reply form only for the unanswered review", async () => {
    stub(reviewsApi([review(), review({ id: "demo-r2", reply: { text: "Dziękuję", repliedAt: "2027-03-02T10:00:00Z" } })]));
    await mount(<PractitionerReviews replyEnabled />);
    expect(host.querySelectorAll("form")).toHaveLength(1);
    expect(host.textContent).toContain("Dziękuję");
  });

  it("the form is absent after a successful reply in the list", async () => {
    stub(reviewsApi([review()]));
    await mount(<PractitionerReviews replyEnabled />);
    await type(area(), "Dziękuję za opinię!");
    await click(button(mr("reply.submit")));
    expect(host.querySelector("form")).toBeNull();
    expect(host.textContent).toContain("Dziękuję za opinię!");
  });

  it("rejects 9 characters, posts 10 to /reviews/{id}/reply, and the form is gone after the reply", async () => {
    stub(() => Response.json({}, { status: 200 }));
    const onReplied = vi.fn();
    await mount(<ReplyForm reviewId="demo-r1" onReplied={onReplied} />);
    await type(area(), "x".repeat(9));
    await click(button(mr("reply.submit")));
    expect(calls).toHaveLength(0);
    expect(host.textContent).toContain(mr("reply.tooShort"));
    await type(area(), "x".repeat(10));
    await click(button(mr("reply.submit")));
    expect(calls[0].url).toBe("/api/proxy/reputation/api/v1/reviews/demo-r1/reply");
    expect(JSON.parse(String(calls[0].init.body))).toEqual({ text: "x".repeat(10) });
    expect(onReplied).toHaveBeenCalledWith("x".repeat(10));
    expect(host.querySelector("form")).toBeNull();
  });

  it("409 already_replied removes the form and says a reply exists", async () => {
    stub(() => problem(409, "already_replied"));
    await mount(<ReplyForm reviewId="demo-r1" onReplied={() => {}} />);
    await type(area(), "x".repeat(12));
    await click(button(mr("reply.submit")));
    expect(host.querySelector("form")).toBeNull();
    expect(host.textContent).toContain(mr("reply.already"));
  });
});
