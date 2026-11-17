import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { feeBpsFrom, formatBps } from "./fee";
import { PricingPage } from "./PricingPage";
import { ts } from "./strings";

describe("fee configuration", () => {
  it("defaults to 1500 bps (D-23) and reads Billing__FirstConsultationFeeBps", () => {
    expect(feeBpsFrom({})).toBe(1500);
    expect(feeBpsFrom({ Billing__FirstConsultationFeeBps: "1250" })).toBe(1250);
  });
  it.each(["", "abc", "-5", "10001", "12.5"])("falls back to the default for %j", (v) => {
    expect(feeBpsFrom({ Billing__FirstConsultationFeeBps: v })).toBe(1500);
  });
  it("formats a rate in percent with a decimal comma", () => {
    expect(formatBps(1500)).toBe("15%");
    expect(formatBps(1250)).toBe("12,5%");
    expect(formatBps(0)).toBe("0%");
  });
});

describe("/cennik", () => {
  it("states the rate from configuration and that only the first consultation is charged", () => {
    const out = renderToStaticMarkup(PricingPage({ feeBps: 1250 }));
    expect(out).toContain("12,5%");
    expect(out).toContain(ts("pricing.onlyFirst"));
    expect((out.match(/<h1[\s>]/g) ?? []).length).toBe(1);
  });
  it("works the fee out on an example price with the half-up rule (25000 gives 3750)", () => {
    const out = renderToStaticMarkup(PricingPage({ feeBps: 1500 }));
    expect(out).toContain("37,50");
    expect(out).toContain("212,50");
  });
});
