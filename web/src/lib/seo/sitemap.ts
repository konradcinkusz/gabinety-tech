import type { MetadataRoute } from "next";
import type { SitemapDto } from "./publicApi";

/** Sitemap entries from GET /api/v1/sitemap: every approved practitioner and only the categories the API marks indexable. */
export function buildSitemap(baseUrl: string, dto: SitemapDto): MetadataRoute.Sitemap {
  const categories = dto.categories.filter((c) => c.indexable).map((c) => ({ url: baseUrl + c.path }));
  const practitioners = dto.practitioners.map((p) => ({ url: `${baseUrl}/gabinet/${p.slug}`, lastModified: new Date(p.lastModified) }));
  return [...categories, ...practitioners];
}

/** Paths crawlers must not visit (SPEC 6.5). */
export const DISALLOWED_PATHS = ["/moje", "/gabinet-programisty", "/admin", "/rezerwacja", "/checkout", "/api"] as const;

export function buildRobots(baseUrl: string): MetadataRoute.Robots {
  return { rules: { userAgent: "*", allow: "/", disallow: [...DISALLOWED_PATHS] }, sitemap: `${baseUrl}/sitemap.xml` };
}
