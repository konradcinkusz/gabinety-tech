import type { Metadata } from "next";
import { LegalPage } from "@/components/static/LegalPage";
import { loadLegal } from "@/components/static/legal";
import { ts } from "@/components/static/strings";
import { readConfig } from "@/lib/config";

export function generateMetadata(): Metadata {
  const title = ts("legal.termsMetaTitle");
  const description = ts("legal.termsMetaDescription");
  const url = readConfig().siteBaseUrl + "/regulamin";
  return { title, description, alternates: { canonical: url }, openGraph: { title, description, url, type: "website", locale: "pl_PL" } };
}

export default function Page() {
  return <LegalPage kind="terms" doc={loadLegal("regulamin")} />;
}
