import type { MetadataRoute } from "next";
import { readConfig } from "@/lib/config";
import { getSitemap } from "@/lib/seo/publicApi";
import { buildSitemap } from "@/lib/seo/sitemap";

export const dynamic = "force-dynamic"; // backend and site address are runtime configuration; the Directory fetch is cached for 5 minutes (REVALIDATE_SECONDS)

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  return buildSitemap(readConfig().siteBaseUrl, await getSitemap());
}
