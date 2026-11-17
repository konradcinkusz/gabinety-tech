import type { MetadataRoute } from "next";
import { readConfig } from "@/lib/config";
import { buildRobots } from "@/lib/seo/sitemap";

export const dynamic = "force-dynamic"; // the site address is runtime configuration, never baked at build time

export default function robots(): MetadataRoute.Robots {
  return buildRobots(readConfig().siteBaseUrl);
}
