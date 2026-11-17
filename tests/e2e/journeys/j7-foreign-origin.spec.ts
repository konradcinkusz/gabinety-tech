import { expect, test } from "../harness/fixtures";
import { randomUUID } from "node:crypto";
import { urls } from "../harness/env";
import { addDays, bff, day, openSlots, publicProfile, registerDemoUser, uniqueDemoName } from "../harness/booking-support";
import { restoreClock } from "../harness/clock";
import { resetAndReseed } from "../harness/demo-data";

// J7 (SPEC 10, 11): a state-changing request through the web origin with a foreign Origin, or from a cross-site fetch, is refused with 403
// `cross_site_request` (CONTRACTS 10.1) and changes nothing: no booking exists afterwards. A same-origin request with the same body is the control.
test.describe("J7 foreign Origin is refused", () => {
  let failed = false;

  // CI diagnostics: the list reporter prints failure details only at the very end of the run, so say why a test failed as it happens (assertion text only).
  test.afterEach(async ({}, testInfo) => {
    if (testInfo.status !== testInfo.expectedStatus) {
      failed = true;
      console.log(`[${testInfo.title}] ${(testInfo.error?.message ?? "failed").split("\n").slice(0, 12).join(" | ")}`);
    }
  });

  // Every row this journey writes is unique to it (a fresh `demo-` client and its bookings). CI runs on a fresh stack, so the 1 to 6 minute reseed
  // only runs on request, for a local re-run: E2E_RESEED=1 (README).
  test.afterAll(async () => {
    test.setTimeout(11 * 60_000);
    await restoreClock();
    if (process.env.E2E_RESEED === "1" && !failed) await resetAndReseed();
  });

  test("a foreign Origin and a cross-site fetch get 403 and create no booking", async ({ context, clock }) => {
    await registerDemoUser(context, uniqueDemoName("j7"));
    const profile = await publicProfile("demo-celina-przykladowska");
    const offering = profile.offerings[0];
    const now = new Date((await clock.read()).now);
    const from = addDays(now, 4);
    const [slot] = await openSlots(profile.id, offering.id, day(from), day(addDays(from, 10)));
    const body = {
      offeringId: offering.id,
      startUtc: slot.toISOString().replace(/\.\d{3}Z$/, "Z"),
      brief: { text: "Opis testowy dla zapytania z obcego źródła, dane syntetyczne.", aiAssisted: false },
    };
    const create = (headers: Record<string, string>) =>
      bff(context.request, "POST", "/api/proxy/booking/api/v1/bookings", { body, headers: { "Idempotency-Key": randomUUID(), ...headers } });
    const myBookings = async () => {
      const res = await bff(context.request, "GET", "/api/proxy/booking/api/v1/me/bookings?role=client");
      expect(res.status()).toBe(200);
      return ((await res.json()) as { items: unknown[] }).items;
    };

    expect(await myBookings()).toHaveLength(0); // a fresh client: nothing to begin with

    for (const [what, headers] of [
      ["foreign Origin", { Origin: "https://evil.example" }],
      ["Sec-Fetch-Site: cross-site", { "Sec-Fetch-Site": "cross-site" }],
      ["foreign Origin and cross-site", { Origin: "https://evil.example", "Sec-Fetch-Site": "cross-site" }],
    ] as const) {
      const res = await create(headers);
      expect(res.status(), what).toBe(403);
      expect(((await res.json()) as { code: string }).code, what).toBe("cross_site_request");
      expect(await myBookings(), `${what}: no booking was created`).toHaveLength(0);
    }

    // control: the same body with the web app's own Origin passes the guard and does create the booking, so the 403s above are the guard and not a bad body
    const own = await create({ Origin: urls.web, "Sec-Fetch-Site": "same-origin" });
    expect(own.status(), "same-origin control").toBe(201);
    expect(await myBookings()).toHaveLength(1);
  });
});
