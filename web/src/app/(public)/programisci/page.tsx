import type { Metadata } from "next";
import { ListPage } from "@/components/practitioners/ListPage";
import { generateListMetadata, type SearchParams } from "@/lib/seo/metadata";

export const revalidate = 300; // 5 minutes (SPEC 6.5); Next requires a literal here
type Props = { searchParams: Promise<SearchParams> };

export async function generateMetadata({ searchParams }: Props): Promise<Metadata> {
  return generateListMetadata({}, await searchParams);
}

export default async function Page({ searchParams }: Props) {
  return <ListPage params={{}} search={await searchParams} />;
}
