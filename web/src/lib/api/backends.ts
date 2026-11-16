import { readConfig } from "../config";
import { backendFetch } from "./http";

// The ladder: Backend__<Name> holds an ordered candidate list (explicit address, service-discovery
// address, internal DNS, localhost). Resolution picks the first candidate that answers and
// remembers it for 30 s; a failure forgets it so the next request walks the ladder again.
const MEMORY_MS = 30_000;
const PROBE_MS = 1_500;
const memory = new Map<string, { url: string; until: number }>();
let now = () => Date.now();

export const resetBackendMemory = () => memory.clear();
export const setClock = (f: () => number) => {
  now = f;
};
export const forgetBackend = (name: string) => memory.delete(name);

async function answers(base: string): Promise<boolean> {
  try {
    await backendFetch(base + "/", { method: "GET", signal: AbortSignal.timeout(PROBE_MS) });
    return true; // any HTTP answer counts, only a connection failure does not
  } catch {
    return false;
  }
}

/** Returns the base URL of a backend, or null when none is configured. */
export async function resolveBackend(name: string): Promise<string | null> {
  const candidates = readConfig().backends[name] ?? [];
  if (candidates.length === 0) return null;
  if (candidates.length === 1) return candidates[0];
  const hit = memory.get(name);
  if (hit && hit.until > now() && candidates.includes(hit.url)) return hit.url;
  for (const c of candidates) {
    if (await answers(c)) {
      memory.set(name, { url: c, until: now() + MEMORY_MS });
      return c;
    }
  }
  return candidates[0]; // none answered: do not remember, the caller will see the failure
}
