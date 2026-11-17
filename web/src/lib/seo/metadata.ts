import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { readConfig } from "../config";
import { tp } from "@/components/practitioners/strings";
import { isVariant, listPath, parseListState, type ListState } from "./listUrl";
import { getSitemap, getTaxonomy, type SitemapDto, type TaxonomyDto } from "./publicApi";

export type RouteParams = { problem?: string; tech?: string };
export type SearchParams = Record<string, string | string[] | undefined>;

export type ListContext = { state: ListState; taxonomy: TaxonomyDto; sitemap: SitemapDto; problemName?: string; techName?: string };

/** Parses the route and loads the taxonomy and sitemap; an unknown problem or tech is a 404. */
export async function loadListContext(params: RouteParams, search: SearchParams): Promise<ListContext> {
  const state = parseListState(params, search);
  const [taxonomy, sitemap] = await Promise.all([getTaxonomy(), getSitemap()]);
  const problemName = state.problem ? taxonomy.problems.find((c) => c.slug === state.problem)?.namePl : undefined;
  const techName = state.tech ? taxonomy.techs.find((c) => c.slug === state.tech)?.namePl : undefined;
  if ((state.problem && !problemName) || (state.tech && !techName)) notFound();
  return { state, taxonomy, sitemap, problemName, techName };
}

export const countFor = (c: ListContext) => c.sitemap.categories.find((x) => x.path === listPath(c.state))?.practitionerCount ?? 0;

/** Indexable from 3 approved practitioners (the API's flag); filtered and paginated variants never are. */
export const isIndexable = (c: ListContext) =>
  !isVariant(c.state) && (c.sitemap.categories.find((x) => x.path === listPath(c.state))?.indexable ?? false);

export function listMetadata(c: ListContext, baseUrl = readConfig().siteBaseUrl): Metadata {
  const count = countFor(c);
  const problem = c.problemName ?? "";
  const tech = c.techName ?? "";
  const [title, description] = c.techName
    ? [tp("meta.techTitle", { problem, tech }), tp("meta.techDescription", { problem, tech, count })]
    : c.problemName
      ? [tp("meta.problemTitle", { problem }), tp("meta.problemDescription", { problem, count })]
      : [tp("meta.allTitle"), tp("meta.allDescription")];
  const url = baseUrl + listPath(c.state); // variants point at the plain list
  return {
    title,
    description,
    alternates: { canonical: url },
    robots: { index: isIndexable(c), follow: true },
    openGraph: { title, description, url, type: "website", locale: "pl_PL" },
  };
}

export async function generateListMetadata(params: RouteParams, search: SearchParams): Promise<Metadata> {
  return listMetadata(await loadListContext(params, search));
}
