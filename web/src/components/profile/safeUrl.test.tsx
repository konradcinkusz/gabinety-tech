import { renderToStaticMarkup } from "react-dom/server";
import { describe, expect, it } from "vitest";
import { ProfilePage } from "./ProfilePage";
import { profileJsonLd } from "./jsonLd";
import { safeHttpsUrl } from "./safeUrl";
import { profile } from "./testFixtures";

// F-09: the server accepts only an https portfolio URL, but a row can be written by another path; the page links only to https.
describe("safeHttpsUrl", () => {
  it.each([
    ["https://demo.example/portfolio", "https://demo.example/portfolio"],
    ["HTTPS://demo.example/a b", "https://demo.example/a%20b"],
  ])("keeps %s", (input, out) => expect(safeHttpsUrl(input)).toBe(out));

  it.each([
    ["javascript:alert(1)"], ["JaVaScRiPt:alert(1)"], ["data:text/html,<b>x</b>"], ["http://demo.example/"], ["ftp://demo.example/"],
    ["//demo.example/"], ["/relative"], ["not a url"], [""], [null], [undefined],
  ])("refuses %s", (input) => expect(safeHttpsUrl(input as string | null | undefined)).toBeNull());
});

describe("a stored portfolio URL that is not https", () => {
  const url = "https://gabinety.example/gabinet/demo-programista-1";

  it("is rendered as a link when it is https", () => {
    const html = renderToStaticMarkup(<ProfilePage profile={profile(1)} url={url} />);
    expect(html).toContain('href="https://demo.example/portfolio"');
  });

  it.each(["javascript:alert(1)", "http://demo.example/p", "data:text/html,x"])("is not a link and not in the JSON-LD: %s", (bad) => {
    const p = profile(1, { portfolioUrl: bad });
    const html = renderToStaticMarkup(<ProfilePage profile={p} url={url} />);
    expect(html).not.toContain(bad);
    expect(html).not.toContain("javascript:");
    expect(JSON.stringify(profileJsonLd(p, url))).not.toContain(bad);
    expect(html).toContain("https://github.com/demo-gh"); // the GitHub link stays
  });

  it("leaves sameAs with the GitHub profile only", () => {
    expect(profileJsonLd(profile(1, { portfolioUrl: "javascript:alert(1)" }), url)["sameAs"]).toEqual(["https://github.com/demo-gh"]);
  });
});
