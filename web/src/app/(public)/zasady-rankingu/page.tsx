import type { Metadata } from "next";
import { RankingRulesPage } from "@/components/static/RankingRulesPage";
import { ts } from "@/components/static/strings";
import { readConfig } from "@/lib/config";

export const dynamic = "force-dynamic"; // reads the Directory at request time (never at build, where it is absent); each fetch is cached for 5 minutes (SPEC 6.5)

export function generateMetadata(): Metadata {
  const title = ts("ranking.metaTitle");
  const description = ts("ranking.metaDescription");
  const url = readConfig().siteBaseUrl + "/zasady-rankingu";
  return { title, description, alternates: { canonical: url }, openGraph: { title, description, url, type: "website", locale: "pl_PL" } };
}

export default RankingRulesPage;
