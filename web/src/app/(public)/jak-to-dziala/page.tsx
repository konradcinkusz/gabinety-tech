import type { Metadata } from "next";
import { HowItWorksPage } from "@/components/static/HowItWorksPage";
import { ts } from "@/components/static/strings";
import { readConfig } from "@/lib/config";

export function generateMetadata(): Metadata {
  const title = ts("how.metaTitle");
  const description = ts("how.metaDescription");
  const url = readConfig().siteBaseUrl + "/jak-to-dziala";
  return { title, description, alternates: { canonical: url }, openGraph: { title, description, url, type: "website", locale: "pl_PL" } };
}

export default HowItWorksPage;
