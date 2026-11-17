// Server-side reads of the public Directory API (CONTRACTS 3.2). Used by the list pages, the sitemap and robots; never by the browser.
// The generated OpenAPI types of T-07.01 predate these endpoints (they only describe /api/v1/me*), so the response shapes are declared here
// until `pnpm gen:types` is re-run against a Directory that serves them (see the PR, "Out-of-scope changes needed").
import { resolveBackend } from "../api/backends";
import { backendFetch } from "../api/http";

export type Money = { amountMinor: number; currency: "PLN" };
export type BadgeKind = "email" | "github" | "nip" | "approved";
export type PractitionerCard = {
  id: string;
  slug: string;
  displayName: string;
  headline: string;
  githubLogin?: string;
  badges: BadgeKind[];
  rating?: { average: number; count: number };
  isNew: boolean;
  fromPrice: Money;
  problemSlugs: string[];
  durations: number[];
};
export type PractitionerPage = { items: PractitionerCard[]; nextCursor?: string };
export type Category = { slug: string; namePl: string };
export type TaxonomyDto = { version: string; problems: Category[]; techs: Category[] };
export type SitemapDto = {
  practitioners: { slug: string; lastModified: string }[];
  categories: { path: string; practitionerCount: number; indexable: boolean }[];
};
export type ListQuery = { problem?: string; tech?: string; q?: string; duration?: number; maxPriceMinor?: number; cursor?: string };

/** Pages are revalidated every 5 minutes (SPEC 6.5); the data cache follows. */
export const REVALIDATE_SECONDS = 300;

export class DirectoryUnavailableError extends Error {
  constructor(readonly status: number) {
    super(`directory_unavailable:${status}`); // a status only: never a body or user text
  }
}

const MAX_RETRY_WAIT_MS = 2_000;
const DEFAULT_RETRY_WAIT_MS = 500;

/** Retry-After in delta-seconds or an HTTP date, bounded to [0, 2 s]; absent or unparsable means a short default. */
function retryWaitMs(res: Response): number {
  const h = res.headers.get("retry-after");
  if (!h) return DEFAULT_RETRY_WAIT_MS;
  const secs = Number(h);
  const ms = Number.isFinite(secs) ? secs * 1000 : Date.parse(h) - Date.now();
  return Number.isNaN(ms) ? DEFAULT_RETRY_WAIT_MS : Math.min(Math.max(ms, 0), MAX_RETRY_WAIT_MS);
}

async function attempt(url: string): Promise<Response> {
  try {
    return await backendFetch(url, {
      headers: { accept: "application/json" },
      signal: AbortSignal.timeout(5_000),
      next: { revalidate: REVALIDATE_SECONDS },
    } as RequestInit);
  } catch {
    throw new DirectoryUnavailableError(0); // timeout or network: never the cause, which could carry a URL or text
  }
}

/**
 * The one server-side read of the Directory for public pages. A 429 (per-client rate limit) is retried once after Retry-After, at most 2 s;
 * every other outcome is returned to the caller, and network failures and timeouts become a DirectoryUnavailableError carrying a status only.
 */
export async function directoryGet(path: string): Promise<Response> {
  const base = await resolveBackend("directory");
  if (!base) throw new DirectoryUnavailableError(0);
  const res = await attempt(base + path);
  if (res.status !== 429) return res;
  await new Promise((r) => setTimeout(r, retryWaitMs(res)));
  return attempt(base + path);
}

async function json<T>(path: string): Promise<T> {
  const res = await directoryGet(path);
  if (!res.ok) throw new DirectoryUnavailableError(res.status);
  return (await res.json()) as T;
}

export const getTaxonomy = () => json<TaxonomyDto>("/api/v1/taxonomy");
export const getSitemap = () => json<SitemapDto>("/api/v1/sitemap");

/** Returns null when the Directory rejects the query (400/422, for instance a stale cursor); any other failure throws. */
export async function listPractitioners(query: ListQuery): Promise<PractitionerPage | null> {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== "") p.set(k, String(v));
  const qs = p.toString();
  const res = await directoryGet(`/api/v1/practitioners${qs ? `?${qs}` : ""}`);
  if (res.status === 400 || res.status === 422) return null;
  if (!res.ok) throw new DirectoryUnavailableError(res.status);
  return (await res.json()) as PractitionerPage;
}
