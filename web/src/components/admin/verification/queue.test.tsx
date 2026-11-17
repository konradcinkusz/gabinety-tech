// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AdminPractitionerRow } from "./api";
import { VerificationQueue } from "./VerificationQueue";
import { av } from "./strings";

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
const flush = () => act(async () => { await Promise.resolve(); await Promise.resolve(); });
const button = (re: RegExp) => [...host.querySelectorAll("button")].find((b) => re.test(b.textContent ?? "")) as HTMLButtonElement | undefined;
const row = (o: Partial<AdminPractitionerRow> = {}): AdminPractitionerRow => ({
  id: "demo-p1", slug: "demo-jan", displayName: "Demo Jan", headline: "Demo: backend", status: "Submitted", statusReason: null,
  githubLinked: true, githubLogin: "demo-jan", nipValid: false, createdAt: "2026-11-01T10:00:00Z", submittedAt: "2026-11-02T10:00:00Z", ...o,
});
const stubFetch = (res: () => Response) => {
  const f = vi.fn(async (_u: string, _i?: RequestInit) => res());
  vi.stubGlobal("fetch", f);
  return f;
};

describe("facts and actions", () => {
  it("shows the facts the moderator needs", () => {
    render(<VerificationQueue items={[row()]} />);
    expect(host.textContent).toContain("Demo Jan");
    expect(host.textContent).toContain("Demo: backend");
    expect(host.querySelector('a[href="https://github.com/demo-jan"]')).not.toBeNull();
    expect(host.textContent).toContain(av("fact.nipInvalid"));
    expect(host.querySelector("time")?.getAttribute("datetime")).toBe("2026-11-02T10:00:00Z");
  });
  it.each([
    ["Submitted", [/zatwierdź/i, /odrzuć/i], [/zawieś/i, /przywróć/i]],
    ["Approved", [/zawieś/i], [/zatwierdź/i, /odrzuć/i, /przywróć/i]],
    ["Suspended", [/przywróć/i], [/zatwierdź/i, /odrzuć/i, /zawieś/i]],
    ["Rejected", [], [/zatwierdź/i, /odrzuć/i, /zawieś/i, /przywróć/i]],
  ] as const)("a %s practitioner is offered only the transitions its status allows", (status, shown, hidden) => {
    render(<VerificationQueue items={[row({ status })]} />);
    for (const re of shown) expect(button(re)).toBeDefined();
    for (const re of hidden) expect(button(re)).toBeUndefined();
  });
});

describe("approve", () => {
  it("posts to the BFF admin route, then reloads from the server", async () => {
    const f = stubFetch(() => new Response(null, { status: 204 }));
    render(<VerificationQueue items={[row()]} />);
    await act(async () => button(/zatwierdź/i)!.click());
    await flush();
    expect(f.mock.calls[0][0]).toBe("/api/proxy/directory/api/v1/admin/practitioners/demo-p1/approve");
    expect(f.mock.calls[0][1]?.method).toBe("POST");
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});

describe("reject needs a reason of 10 to 500 characters", () => {
  const open = async () => {
    const f = stubFetch(() => new Response(null, { status: 204 }));
    render(<VerificationQueue items={[row()]} />);
    await act(async () => button(/odrzuć/i)!.click());
    return f;
  };
  it("a short reason sends nothing and says why", async () => {
    const f = await open();
    await act(async () => { await userEvent.type(host.querySelector("textarea")!, "za krótko"); });
    await act(async () => button(new RegExp(av("action.confirmReject"), "i"))!.click());
    expect(f).not.toHaveBeenCalled();
    expect(host.textContent).toContain(av("reason.invalid"));
    expect(refresh).not.toHaveBeenCalled();
  });
  it("a valid reason is sent trimmed in the body", async () => {
    const f = await open();
    await act(async () => { await userEvent.type(host.querySelector("textarea")!, "  Brak publicznych repozytoriów  "); });
    await act(async () => button(new RegExp(av("action.confirmReject"), "i"))!.click());
    await flush();
    expect(f.mock.calls[0][0]).toContain("/demo-p1/reject");
    expect(JSON.parse(String(f.mock.calls[0][1]?.body))).toEqual({ reason: "Brak publicznych repozytoriów" });
    expect(refresh).toHaveBeenCalledTimes(1);
  });
});

describe("answers of the API", () => {
  it("409 invalid_transition: 'state changed, refresh', and the list reloads", async () => {
    stubFetch(() => Response.json({ code: "invalid_transition" }, { status: 409 }));
    render(<VerificationQueue items={[row()]} />);
    await act(async () => button(/zatwierdź/i)!.click());
    await flush();
    expect(host.textContent).toContain(av("error.stateChanged"));
    expect(refresh).toHaveBeenCalledTimes(1);
  });
  it("403: a forbidden message, no reload", async () => {
    stubFetch(() => new Response(null, { status: 403 }));
    render(<VerificationQueue items={[row()]} />);
    await act(async () => button(/zatwierdź/i)!.click());
    await flush();
    expect(host.textContent).toContain(av("error.forbidden"));
    expect(refresh).not.toHaveBeenCalled();
  });
  it("never logs", async () => {
    const spies = (["log", "warn", "error"] as const).map((m) => vi.spyOn(console, m).mockImplementation(() => {}));
    stubFetch(() => Response.json({ code: "x" }, { status: 500 }));
    render(<VerificationQueue items={[row()]} />);
    await act(async () => button(/odrzuć/i)!.click());
    await act(async () => { await userEvent.type(host.querySelector("textarea")!, "Tajny powód odrzucenia"); });
    await act(async () => button(new RegExp(av("action.confirmReject"), "i"))!.click());
    await flush();
    for (const s of spies) expect(s).not.toHaveBeenCalled();
    spies.forEach((s) => s.mockRestore());
  });
});
