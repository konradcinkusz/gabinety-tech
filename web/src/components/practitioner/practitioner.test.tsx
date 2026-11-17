// @vitest-environment jsdom
import userEvent from "@testing-library/user-event";
import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Availability } from "./availability";
import type { OfferingInput, OfferingPrivate, PractitionerApi, PractitionerPrivate, Problem, ProfileInput, Result, Taxonomy } from "./api";
import { AvailabilityEditor } from "./AvailabilityEditor";
import { CreateProfile } from "./CreateProfile";
import { OfferingsManager } from "./OfferingsManager";
import { Onboarding } from "./Onboarding";
import { ProfileEditor } from "./ProfileEditor";
import { pp } from "./strings";

(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

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
});
const render = (el: React.ReactElement) => act(() => root.render(el));
const flush = () => act(async () => { await Promise.resolve(); });
const byLabel = (re: RegExp) => [...host.querySelectorAll("label")].find((l) => re.test(l.textContent ?? ""))!.control as HTMLInputElement;
const button = (re: RegExp) => [...host.querySelectorAll("button")].find((b) => re.test(b.textContent ?? "")) as HTMLButtonElement;
/** The text of the error shown for a field: whatever its aria-describedby points at, minus the hint. */
const errorOf = (field: HTMLElement) =>
  (field.getAttribute("aria-describedby") ?? "").split(" ").map((id) => document.getElementById(id)).filter((n) => n?.id.endsWith("-error")).map((n) => n!.textContent).join(" ");

const ok = <T,>(value: T): Result<T> => ({ ok: true, value });
const fail = (status: number, code: string, errors: Record<string, string[]> = {}, unmetItems: string[] = []): Result<never> => ({ ok: false, problem: { status, code, errors, unmetItems } satisfies Problem });
const profile = (o: Partial<PractitionerPrivate> = {}): PractitionerPrivate => ({
  id: "demo-p1", slug: "demo-jan", status: "Draft", displayName: "Demo Jan", headline: "Demo: Senior backend developer", bio: "x".repeat(250),
  portfolioUrl: "https://demo.example.test", meetingUrlDefault: null, nip: null, emailVerified: true, githubLinked: false, nipValid: false, githubLogin: null, ...o,
});
const offering = (o: Partial<OfferingPrivate> = {}): OfferingPrivate => ({
  id: "demo-o1", title: "Demo konsultacja", description: "", durationMinutes: 60, price: { amountMinor: 25000, currency: "PLN" }, problemSlug: "demo-problem", techSlugs: [], isActive: true, ...o,
});
const taxonomy: Taxonomy = { version: "demo", problems: [{ slug: "demo-problem", namePl: "Demo problem" }], techs: [{ slug: "demo-tech", namePl: "Demo tech" }] };

function fakeApi(over: Partial<PractitionerApi> = {}) {
  const base = {
    getProfile: vi.fn(async () => ok(profile())),
    createProfile: vi.fn(async () => ok(profile())),
    saveProfile: vi.fn(async (_i: ProfileInput) => ok(profile())),
    submit: vi.fn(async () => ok(undefined)),
    requestGithubLink: vi.fn(async () => ok({ code: "gabinety-verify-DEMO123456", instruction: "demo" })),
    verifyGithubLink: vi.fn(async () => ok(undefined)),
    listOfferings: vi.fn(async () => ok([offering()])),
    saveOffering: vi.fn(async (_id: string | null, i: OfferingInput) => ok(offering({ price: { amountMinor: i.priceMinor, currency: "PLN" } }))),
    deactivateOffering: vi.fn(async () => ok(undefined)),
    getAvailability: vi.fn(async () => ok<Availability>({ rules: [], exceptions: [] })),
    saveAvailability: vi.fn(async (a: Availability) => ok(a)),
    getTaxonomy: vi.fn(async () => ok(taxonomy)),
  };
  return { ...base, ...over } as typeof base & PractitionerApi;
}

describe("OfferingsManager: price in PLN, sent as grosz", () => {
  async function fillAndSave(price: string, api = fakeApi()) {
    const user = userEvent.setup();
    render(<OfferingsManager initial={[]} taxonomy={taxonomy} api={api} />);
    await user.type(byLabel(/Nazwa oferty/), "Demo konsultacja");
    await user.type(byLabel(/Cena/), price);
    await user.selectOptions(byLabel(/Rodzaj problemu/), "demo-problem");
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    return api;
  }
  it.each([["250", 25000], ["250,50", 25050]])("price %s becomes %i grosz", async (typed, grosz) => {
    const api = await fillAndSave(typed);
    expect(api.saveOffering).toHaveBeenCalledTimes(1);
    expect(api.saveOffering).toHaveBeenCalledWith(null, expect.objectContaining({ priceMinor: grosz, durationMinutes: 30, problemSlug: "demo-problem" }));
  });
  it("rejects 250,555: nothing is sent and the price field says why", async () => {
    const api = await fillAndSave("250,555");
    expect(api.saveOffering).not.toHaveBeenCalled();
    expect(errorOf(byLabel(/Cena/))).toBe(pp("offerings.price_format"));
  });
  it("rejects a price outside 50 to 1500 zł before sending", async () => {
    const api = await fillAndSave("49");
    expect(api.saveOffering).not.toHaveBeenCalled();
    expect(errorOf(byLabel(/Cena/))).toBe(pp("offerings.price_range"));
  });
  it("shows the server's per-field errors and too_many_offerings", async () => {
    const api = await fillAndSave("250", fakeApi({ saveOffering: vi.fn(async () => fail(422, "validation_failed", { title: ["5 to 80 characters"] })) }));
    expect(errorOf(byLabel(/Nazwa oferty/))).toBe(pp("fieldErrors.title"));
    const api2 = fakeApi({ saveOffering: vi.fn(async () => fail(422, "too_many_offerings")) });
    act(() => root.unmount()); root = createRoot(host);
    await fillAndSave("250", api2);
    expect(host.textContent).toContain(pp("offerings.tooMany"));
    expect(api).toBeDefined();
  });
  it("lists offerings with the price formatted from grosz and edits one in place (PUT by id)", async () => {
    const api = fakeApi();
    const user = userEvent.setup();
    render(<OfferingsManager initial={[offering({ price: { amountMinor: 25050, currency: "PLN" } })]} taxonomy={taxonomy} api={api} />);
    expect(host.textContent).toContain("250,50");
    await user.click(button(new RegExp(pp("offerings.edit"))));
    expect(byLabel(/Cena/).value).toBe("250,50");
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(api.saveOffering).toHaveBeenCalledWith("demo-o1", expect.objectContaining({ priceMinor: 25050 }));
  });
});

describe("ProfileEditor", () => {
  it("shows live character counts", async () => {
    const user = userEvent.setup();
    render(<ProfileEditor initial={profile({ headline: "", bio: "" })} api={fakeApi()} />);
    expect(host.textContent).toContain("0 / 120");
    await user.type(byLabel(/Nagłówek/), "abcde");
    expect(host.textContent).toContain("5 / 120");
    await user.type(byLabel(/Opis/), "abc");
    expect(host.textContent).toContain("3 / 2000");
  });
  it("puts the server's contact_details_not_allowed error on the field that holds the contact detail", async () => {
    const api = fakeApi({ saveProfile: vi.fn(async () => fail(422, "contact_details_not_allowed")) });
    const user = userEvent.setup();
    render(<ProfileEditor initial={profile()} api={api} />);
    const headline = byLabel(/Nagłówek/);
    await user.clear(headline);
    await user.type(headline, "Napisz: demo-jan@example.test");
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(errorOf(byLabel(/Nagłówek/))).toBe(pp("fieldErrors.contact_details_not_allowed"));
    expect(errorOf(byLabel(/Opis/))).toBe("");
  });
  it("when no field can be told apart the error goes on every free-text field, never on none", async () => {
    const api = fakeApi({ saveProfile: vi.fn(async () => fail(422, "contact_details_not_allowed")) });
    const user = userEvent.setup();
    render(<ProfileEditor initial={profile()} api={api} />);
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(errorOf(byLabel(/Nagłówek/))).toBe(pp("fieldErrors.contact_details_not_allowed"));
    expect(errorOf(byLabel(/Opis/))).toBe(pp("fieldErrors.contact_details_not_allowed"));
  });
  it("shows validation_failed errors per field, from the server's map", async () => {
    const api = fakeApi({ saveProfile: vi.fn(async () => fail(422, "validation_failed", { nip: ["ten digits with a valid checksum"], portfolioUrl: ["x"] })) });
    const user = userEvent.setup();
    render(<ProfileEditor initial={profile()} api={api} />);
    await user.type(byLabel(/NIP/), "5260250996");
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(errorOf(byLabel(/NIP/))).toBe(pp("fieldErrors.nip"));
    expect(errorOf(byLabel(/portfolio/))).toBe(pp("fieldErrors.portfolioUrl"));
    expect(errorOf(byLabel(/Nagłówek/))).toBe("");
  });
  it("omits empty optional fields from the request (no null, CONTRACTS 0)", async () => {
    const api = fakeApi();
    const user = userEvent.setup();
    render(<ProfileEditor initial={profile({ portfolioUrl: null })} api={api} />);
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    const sent = api.saveProfile.mock.calls[0][0];
    expect(Object.keys(sent).sort()).toEqual(["bio", "displayName", "headline"]);
  });
});

describe("AvailabilityEditor", () => {
  const addRule = async (user: ReturnType<typeof userEvent.setup>) => user.click(button(new RegExp(pp("availability.addRule"))));
  it("reports an overlap before submit and does not send", async () => {
    const api = fakeApi();
    const user = userEvent.setup();
    render(<AvailabilityEditor initial={{ rules: [], exceptions: [] }} api={api} />);
    await addRule(user);
    await addRule(user); // two identical Monday windows
    expect(host.textContent).toContain(pp("availability.issues.overlap", { day: "poniedziałek" }));
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(api.saveAvailability).not.toHaveBeenCalled();
  });
  it("offers only :00 and :30 and sends a valid week as is", async () => {
    const api = fakeApi();
    const user = userEvent.setup();
    render(<AvailabilityEditor initial={{ rules: [{ isoWeekday: 3, startLocal: "09:00", endLocal: "12:30" }], exceptions: [{ dateLocal: "2027-01-06" }] }} api={api} />);
    const times = [...host.querySelectorAll("select")].flatMap((s) => [...s.options].map((o) => o.value)).filter((v) => /:/.test(v));
    expect(times.every((v) => /^\d\d:(00|30)$/.test(v))).toBe(true);
    expect(times).not.toContain("24:00");
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(api.saveAvailability).toHaveBeenCalledWith({ rules: [{ isoWeekday: 3, startLocal: "09:00", endLocal: "12:30" }], exceptions: [{ dateLocal: "2027-01-06" }] });
    expect(host.textContent).toContain(pp("common.saved"));
  });
  it("shows the server's errors when it refuses anyway", async () => {
    const api = fakeApi({ saveAvailability: vi.fn(async () => fail(422, "validation_failed", { "rules[0].endLocal": ["must be after startLocal"] })) });
    const user = userEvent.setup();
    render(<AvailabilityEditor initial={{ rules: [{ isoWeekday: 1, startLocal: "09:00", endLocal: "12:00" }], exceptions: [] }} api={api} />);
    await user.click(button(new RegExp(pp("common.save"))));
    await flush();
    expect(host.textContent).toContain(pp("availability.issues.end_before_start"));
  });
  it("adds exception dates, lists them, and refuses the same day twice", async () => {
    const user = userEvent.setup();
    render(<AvailabilityEditor initial={{ rules: [], exceptions: [] }} api={fakeApi()} />);
    for (let i = 0; i < 2; i++) {
      await user.type(byLabel(/Data dnia wolnego/), "2027-03-01");
      await user.click(button(new RegExp(pp("availability.addException"))));
    }
    expect(host.textContent).toContain("2027-03-01");
    expect(host.textContent).toContain(pp("availability.issues.duplicate_exception"));
  });
});

describe("Onboarding: the checklist is the API's, not the form's", () => {
  const item = (key: string) => host.querySelector(`[data-item="${key}"]`)!;
  it("marks items from the profile and offerings it was given", () => {
    render(<Onboarding initialProfile={profile({ githubLinked: false, nipValid: true })} initialOfferings={[offering({ isActive: false })]} api={fakeApi()} />);
    expect(item("headline").getAttribute("data-done")).toBe("true");
    expect(item("github").getAttribute("data-done")).toBe("false");
    expect(item("nip").getAttribute("data-done")).toBe("true");
    expect(item("offering").getAttribute("data-done")).toBe("false");
  });
  it("a successful verify call does not tick GitHub until the API says so", async () => {
    let linked = false;
    const api = fakeApi({ getProfile: vi.fn(async () => ok(profile({ githubLinked: linked, githubLogin: linked ? "demo-jan" : null }))), verifyGithubLink: vi.fn(async () => ok(undefined)) });
    const user = userEvent.setup();
    render(<Onboarding initialProfile={profile()} initialOfferings={[offering()]} api={api} />);
    await user.type(byLabel(/Login GitHub/), "demo-jan");
    await user.click(button(new RegExp(pp("github.request"))));
    await flush();
    expect(host.textContent).toContain("gabinety-verify-DEMO123456");
    await user.click(button(new RegExp(pp("github.verify"))));
    await flush();
    expect(api.verifyGithubLink).toHaveBeenCalled();
    expect(item("github").getAttribute("data-done")).toBe("false"); // verify said 204, the profile still says not linked
    linked = true;
    await user.click(button(new RegExp(pp("github.verify"))));
    await flush();
    expect(item("github").getAttribute("data-done")).toBe("true");
  });
  it("shows the rejection reason and the status", () => {
    render(<Onboarding initialProfile={profile({ status: "Rejected", statusReason: "Demo: link do portfolio nie działa" })} initialOfferings={[]} api={fakeApi()} />);
    expect(host.textContent).toContain(pp("status.Rejected"));
    expect(host.textContent).toContain("Demo: link do portfolio nie działa");
  });
  it("lists the items the server says are unmet when submit is refused with 422 submit_incomplete", async () => {
    const api = fakeApi({ submit: vi.fn(async () => fail(422, "submit_incomplete", {}, ["github_linked", "nip_valid"])) });
    const user = userEvent.setup();
    render(<Onboarding initialProfile={profile()} initialOfferings={[offering()]} api={api} />);
    await user.click(button(new RegExp(pp("onboarding.submit"))));
    await flush();
    expect(host.textContent).toContain(pp("onboarding.items.github"));
    expect(host.textContent).toContain(pp("onboarding.items.nip"));
    expect(host.textContent).toContain("Brakuje");
  });
  it("after a successful submit it re-reads the profile and shows the new status", async () => {
    let status: PractitionerPrivate["status"] = "Draft";
    const api = fakeApi({ getProfile: vi.fn(async () => ok(profile({ status }))), submit: vi.fn(async () => { status = "Submitted"; return ok(undefined); }) });
    const user = userEvent.setup();
    render(<Onboarding initialProfile={profile()} initialOfferings={[offering()]} api={api} />);
    await user.click(button(new RegExp(pp("onboarding.submit"))));
    await flush();
    expect(host.textContent).toContain(pp("status.Submitted"));
  });
});

describe("CreateProfile: the 'create your gabinet' step", () => {
  it("creates the profile from a display name and moves on", async () => {
    const api = fakeApi();
    const navigate = vi.fn();
    const user = userEvent.setup();
    render(<CreateProfile api={api} navigate={navigate} />);
    await user.type(byLabel(/Nazwa wyświetlana/), "Demo Jan");
    await user.click(button(new RegExp(pp("create.submit"))));
    await flush();
    expect(api.createProfile).toHaveBeenCalledWith("Demo Jan");
    expect(navigate).toHaveBeenCalledWith("/gabinet-programisty");
  });
  it("shows practitioner_exists and a failure", async () => {
    const api = fakeApi({ createProfile: vi.fn(async () => fail(409, "practitioner_exists")) });
    const user = userEvent.setup();
    render(<CreateProfile api={api} navigate={() => {}} />);
    await user.type(byLabel(/Nazwa wyświetlana/), "Demo Jan");
    await user.click(button(new RegExp(pp("create.submit"))));
    await flush();
    expect(host.textContent).toContain(pp("create.exists"));
  });
});
