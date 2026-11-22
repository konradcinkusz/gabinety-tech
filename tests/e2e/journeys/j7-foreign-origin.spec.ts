import { expect, test } from "../harness/fixtures";
import { demoPassword, emailOf, urls } from "../harness/env";

// J7 (SPEC 10, 11) on the Blazor host (ADR-0010). The browser never calls a backend here (no /api/proxy), so the expectations
// are checked on the one state-changing endpoint a visitor can reach: the sign-in POST. A foreign Origin or a cross-site fetch is refused with 403
// `cross_site_request` (CONTRACTS 10.1) BEFORE antiforgery or the form, and changes nothing: no session cookie. The same-origin control proves the 403s are the guard.
test.describe("J7 foreign Origin is refused", () => {
  test("the sign-in POST with a foreign Origin or a cross-site fetch gets 403 and sets no session", async ({ context }) => {
    const page = await context.request.get("/logowanie");
    expect(page.status()).toBe(200);
    const html = await page.text();
    const token = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(html)?.[1];
    expect(token).toBeTruthy();
    const form = { _handler: "login", __RequestVerificationToken: token!, "Model.Email": emailOf("demo-client"), "Model.Password": demoPassword };
    const post = (headers: Record<string, string>) => context.request.post("/logowanie", { form, headers, maxRedirects: 0 });
    const signedIn = async () => (await context.cookies()).some((c) => c.name === "gt_session");

    expect(await signedIn()).toBe(false);
    for (const [what, headers] of [
      ["foreign Origin", { Origin: "https://evil.example" }],
      ["Sec-Fetch-Site: cross-site", { "Sec-Fetch-Site": "cross-site" }],
      ["foreign Origin and cross-site", { Origin: "https://evil.example", "Sec-Fetch-Site": "cross-site" }],
    ] as const) {
      const res = await post(headers);
      expect(res.status(), what).toBe(403);
      expect(((await res.json()) as { code: string }).code, what).toBe("cross_site_request");
      expect(await signedIn(), `${what}: nothing changed, no session`).toBe(false);
    }

    // control: the same form with the host's own Origin passes the guard and signs in
    const own = await post({ Origin: urls.web, "Sec-Fetch-Site": "same-origin" });
    expect([302, 303], "same-origin control").toContain(own.status());
    expect(await signedIn()).toBe(true);
  });
});
