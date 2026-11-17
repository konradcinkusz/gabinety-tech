import type { Metadata } from "next";
import { HomePage } from "@/components/static/HomePage";
import { ts } from "@/components/static/strings";
import { readConfig } from "@/lib/config";

export const dynamic = "force-dynamic"; // reads the Directory at request time (never at build, where it is absent); each fetch is cached for 5 minutes (SPEC 6.5)

export function generateMetadata(): Metadata {
  const title = ts("home.metaTitle");
  const description = ts("home.metaDescription");
  const url = readConfig().siteBaseUrl + "/";
  return { title, description, alternates: { canonical: url }, openGraph: { title, description, url, type: "website", locale: "pl_PL" } };
}

// The root layout already provides <main id="main">: a second main landmark here would nest one in the other.
export default HomePage;
