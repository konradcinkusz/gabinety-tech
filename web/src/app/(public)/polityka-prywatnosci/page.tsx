import type { Metadata } from "next";
import { LegalPage } from "@/components/static/LegalPage";
import { loadLegal } from "@/components/static/legal";
import { ts } from "@/components/static/strings";
import { readConfig } from "@/lib/config";

export function generateMetadata(): Metadata {
  const title = ts("legal.privacyMetaTitle");
  const description = ts("legal.privacyMetaDescription");
  const url = readConfig().siteBaseUrl + "/polityka-prywatnosci";
  return { title, description, alternates: { canonical: url }, openGraph: { title, description, url, type: "website", locale: "pl_PL" } };
}

export default function Page() {
  return <LegalPage kind="privacy" doc={loadLegal("polityka-prywatnosci")} />;
}
