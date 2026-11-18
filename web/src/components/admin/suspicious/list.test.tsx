import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import type { SuspiciousPair } from "./api";
import { PairList } from "./PairList";
import { sp } from "./strings";

const pair = (o: Partial<SuspiciousPair> = {}): SuspiciousPair => ({ pairKey: "demo-c1:demo-p1", clientUserId: "demo-c1", practitionerId: "demo-p1", bookings: 4, reviews: 3, ...o });

describe("PairList", () => {
  it("says there is nothing to look at when the list is empty", () => {
    const html = renderToStaticMarkup(<PairList items={[]} />);
    expect(html).toContain(sp("empty"));
  });
  it("shows the two ids and the two counts of each pair, in the order given", () => {
    const html = renderToStaticMarkup(<PairList items={[pair(), pair({ pairKey: "k2", clientUserId: "demo-c2", practitionerId: "demo-p2", bookings: 7, reviews: 7 })]} />);
    for (const t of ["demo-c1", "demo-p1", "demo-c2", "demo-p2", sp("fact.client"), sp("fact.practitioner"), sp("fact.bookings"), sp("fact.reviews")]) expect(html).toContain(t);
    expect(html.indexOf("demo-c1")).toBeLessThan(html.indexOf("demo-c2"));
    expect(html).toMatch(/>4</);
    expect(html).toMatch(/>7</);
  });
  it("renders ids as text, never as markup", () => {
    const html = renderToStaticMarkup(<PairList items={[pair({ clientUserId: "<img src=x onerror=alert(1)>" })]} />);
    expect(html).not.toContain("<img");
    expect(html).toContain("&lt;img");
  });
  it("is keyed by the pair, so two pairs with the same client are both shown", () => {
    const html = renderToStaticMarkup(<PairList items={[pair({ pairKey: "a" }), pair({ pairKey: "b", practitionerId: "demo-p9" })]} />);
    expect(html).toContain("demo-p1");
    expect(html).toContain("demo-p9");
  });
});
