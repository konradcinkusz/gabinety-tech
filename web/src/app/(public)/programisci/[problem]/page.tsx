import type { Metadata } from "next";
import { ListPage } from "@/components/practitioners/ListPage";
import { generateListMetadata, type SearchParams } from "@/lib/seo/metadata";

export const revalidate = 300; // 5 minutes (SPEC 6.5); Next requires a literal here
type Props = { params: Promise<{ problem: string }>; searchParams: Promise<SearchParams> };

export async function generateMetadata({ params, searchParams }: Props): Promise<Metadata> {
  return generateListMetadata(await params, await searchParams);
}

export default async function Page({ params, searchParams }: Props) {
  return <ListPage params={await params} search={await searchParams} />;
}
