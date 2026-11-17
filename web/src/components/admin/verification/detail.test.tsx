// @vitest-environment jsdom
import { createRoot, type Root } from "react-dom/client";
import { act } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AdminPractitionerDetail, AdminVerificationEvent } from "./api";
import { PractitionerDetail } from "./PractitionerDetail";
import { av } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
let host: HTMLElement;
let root: Root;
beforeEach(() => { host = document.createElement("div"); document.body.appendChild(host); root = createRoot(host); });
afterEach(() => { act(() => root.unmount()); host.remove(); });
const render = (el: React.ReactElement) => act(() => root.render(el));

const detail = (o: Partial<AdminPractitionerDetail> = {}): AdminPractitionerDetail => ({
  id: "demo-p1", slug: "demo-jan", displayName: "Demo Jan", headline: "Demo: backend", bio: "Demo <b>bio</b>", status: "Rejected", statusReason: "demo powód odrzucenia",
  githubLinked: true, githubLogin: "demo-jan", nip: "1234563218", nipValid: true, portfolioUrl: "https://demo.example/jan",
  createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-03T10:00:00Z", submittedAt: "2026-11-02T10:00:00Z", approvedAt: null, ...o,
});
const ev = (o: Partial<AdminVerificationEvent> = {}): AdminVerificationEvent => ({ id: "e1", kind: "Submitted", actorUserId: "demo-u1", note: null, at: "2026-11-02T10:00:00Z", ...o });

describe("practitioner data", () => {
  it("shows the facts, the status and the reason", () => {
    render(<PractitionerDetail detail={detail()} events={[]} />);
    expect(host.querySelector("h1")?.textContent).toBe("Demo Jan");
    expect(host.textContent).toContain(av("status.Rejected"));
    expect(host.textContent).toContain("demo powód odrzucenia");
    expect(host.textContent).toContain("1234563218");
    expect(host.querySelector('a[href="https://github.com/demo-jan"]')).not.toBeNull();
    expect(host.querySelector('a[href="https://demo.example/jan"]')?.getAttribute("rel")).toContain("noopener");
  });
  it("renders the bio as text, never as markup", () => {
    render(<PractitionerDetail detail={detail()} events={[]} />);
    expect(host.querySelector("b")).toBeNull();
    expect(host.textContent).toContain("Demo <b>bio</b>");
  });
  it("does not link a portfolio url that is not http(s)", () => {
    render(<PractitionerDetail detail={detail({ portfolioUrl: "javascript:alert(1)" })} events={[]} />);
    expect(host.querySelector('a[href^="javascript"]')).toBeNull();
  });
  it("marks missing optional facts", () => {
    render(<PractitionerDetail detail={detail({ nip: null, nipValid: false, portfolioUrl: null, githubLinked: false, githubLogin: null, statusReason: null })} events={[]} />);
    expect(host.textContent).toContain(av("fact.none"));
    expect(host.textContent).toContain(av("fact.githubNot"));
  });
  it("links back to the queue", () => {
    render(<PractitionerDetail detail={detail()} events={[]} />);
    expect(host.querySelector('a[href="/admin/weryfikacja"]')).not.toBeNull();
  });
});

describe("verification timeline", () => {
  it("is an ordered list in the order given, one <time> per event, with kind in Polish", () => {
    render(<PractitionerDetail detail={detail()} events={[ev(), ev({ id: "e2", kind: "Rejected", note: "demo notatka", at: "2026-11-03T10:00:00Z" })]} />);
    const items = host.querySelectorAll("ol > li");
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toContain(av("event.Submitted"));
    expect(items[1].textContent).toContain(av("event.Rejected"));
    expect(items[1].textContent).toContain("demo notatka");
    expect(items[1].querySelector("time")?.getAttribute("datetime")).toBe("2026-11-03T10:00:00Z");
  });
  it("an unknown kind from a newer backend falls back to a neutral label, never the raw string", () => {
    render(<PractitionerDetail detail={detail()} events={[ev({ kind: "Mystery" as never })]} />);
    expect(host.textContent).not.toContain("Mystery");
    expect(host.textContent).toContain(av("event.unknown"));
  });
  it("shows an empty state and has a heading for the list", () => {
    render(<PractitionerDetail detail={detail()} events={[]} />);
    expect(host.textContent).toContain(av("timeline.empty"));
    expect(host.querySelectorAll("h2").length).toBeGreaterThan(0);
  });
  it("a timeline that could not be loaded is said so, the data still shows", () => {
    render(<PractitionerDetail detail={detail()} events={null} />);
    expect(host.textContent).toContain(av("timeline.unavailable"));
    expect(host.querySelector("h1")?.textContent).toBe("Demo Jan");
  });
});
