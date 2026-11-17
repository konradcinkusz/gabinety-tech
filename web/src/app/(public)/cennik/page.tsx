import type { Metadata } from "next";
import { feeBpsFrom } from "@/components/static/fee";
import { PricingPage } from "@/components/static/PricingPage";
import { ts } from "@/components/static/strings";
import { readConfig } from "@/lib/config";

export const dynamic = "force-dynamic"; // the rate is runtime configuration, never baked at build time

export function generateMetadata(): Metadata {
  const title = ts("pricing.metaTitle");
  const description = ts("pricing.metaDescription");
  const url = readConfig().siteBaseUrl + "/cennik";
  return { title, description, alternates: { canonical: url }, openGraph: { title, description, url, type: "website", locale: "pl_PL" } };
}

export default function Page() {
  return <PricingPage feeBps={feeBpsFrom()} />;
}
