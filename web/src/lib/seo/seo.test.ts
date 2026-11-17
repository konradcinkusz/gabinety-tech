import { afterEach, describe, expect, it } from "vitest";
import { installDirectory, sitemapWith, uninstallDirectory } from "./testFixtures";
import robots from "../../app/robots";
import sitemap from "../../app/sitemap";

afterEach(uninstallDirectory);

describe("sitemap", () => {
  it("lists practitioners and only the indexable category paths", async () => {
    installDirectory({
      items: [],
      sitemap: sitemapWith({ "/programisci": 5, "/programisci/audyt-kodu": 3, "/programisci/audyt-kodu/dotnet": 2, "/programisci/integracja-ai": 1 }),
    });
    const urls = (await sitemap()).map((e) => e.url);
    expect(urls).toContain("https://gabinety.example/programisci");
    expect(urls).toContain("https://gabinety.example/programisci/audyt-kodu");
    expect(urls).toContain("https://gabinety.example/gabinet/demo-programista-1");
    expect(urls).not.toContain("https://gabinety.example/programisci/audyt-kodu/dotnet");
    expect(urls).not.toContain("https://gabinety.example/programisci/integracja-ai");
  });

  it("a category the API marks indexable=false is never listed, whatever its count", async () => {
    const dto = sitemapWith({ "/programisci": 9 });
    dto.categories[0].indexable = false;
    installDirectory({ items: [], sitemap: dto });
    expect((await sitemap()).map((e) => e.url)).toEqual(["https://gabinety.example/gabinet/demo-programista-1"]);
  });
});

describe("robots", () => {
  it("disallows the private areas and points at the sitemap", () => {
    process.env["Site__BaseUrl"] = "https://gabinety.example";
    const r = robots();
    const rule = Array.isArray(r.rules) ? r.rules[0] : r.rules;
    expect(rule.disallow).toEqual(["/moje", "/gabinet-programisty", "/admin", "/rezerwacja", "/checkout", "/api"]);
    expect(r.sitemap).toBe("https://gabinety.example/sitemap.xml");
    delete process.env["Site__BaseUrl"];
  });
});
