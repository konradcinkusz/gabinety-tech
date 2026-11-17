// The state of a list page and the links between its variants. Pure; filters are links, so everything here is plain URLs.
export const DURATIONS = [30, 60] as const;
/** Price ceiling presets offered as links, in grosz (money is an integer grosz everywhere). */
export const PRICE_PRESETS = [15000, 30000, 50000] as const;
const MAX_Q = 60;
const MAX_CURSOR = 512;

export type ListState = {
  problem?: string;
  tech?: string;
  duration?: 30 | 60;
  maxPriceMinor?: number;
  q?: string;
  cursor?: string;
};

type Raw = Record<string, string | string[] | undefined>;
const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);

export function parseListState(path: { problem?: string; tech?: string }, search: Raw): ListState {
  const s: ListState = {};
  if (path.problem) s.problem = path.problem;
  if (path.problem && path.tech) s.tech = path.tech;
  const d = Number(first(search["duration"]));
  if (d === 30 || d === 60) s.duration = d;
  const price = first(search["maxPriceMinor"]);
  if (price && /^[1-9]\d{0,8}$/.test(price)) s.maxPriceMinor = Number(price);
  const q = first(search["q"])?.trim();
  if (q && q.length <= MAX_Q) s.q = q;
  const cursor = first(search["cursor"]);
  if (cursor && cursor.length <= MAX_CURSOR) s.cursor = cursor;
  return s;
}

/** Changes filters and returns to the first page; every filter not named in the patch is kept. */
export function withFilter(state: ListState, patch: Partial<Omit<ListState, "cursor">>): ListState {
  const next: ListState = { ...state, ...patch };
  delete next.cursor;
  if ("problem" in patch && patch.problem !== state.problem && !("tech" in patch)) delete next.tech;
  for (const k of Object.keys(next) as (keyof ListState)[]) if (next[k] === undefined) delete next[k];
  return next;
}

export function listPath(state: Pick<ListState, "problem" | "tech">): string {
  if (!state.problem) return "/programisci";
  return state.tech ? `/programisci/${state.problem}/${state.tech}` : `/programisci/${state.problem}`;
}

export function listHref(state: ListState): string {
  const p = new URLSearchParams();
  if (state.duration) p.set("duration", String(state.duration));
  if (state.maxPriceMinor) p.set("maxPriceMinor", String(state.maxPriceMinor));
  if (state.q) p.set("q", state.q);
  if (state.cursor) p.set("cursor", state.cursor);
  const qs = p.toString();
  return listPath(state) + (qs ? `?${qs}` : "");
}

/** True when the page is a filtered or paginated variant of its category (such variants are not indexed). */
export const isVariant = (s: ListState) => Boolean(s.duration || s.maxPriceMinor || s.q || s.cursor);
