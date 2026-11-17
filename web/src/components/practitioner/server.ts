// Server-side loaders of the practitioner area: the page renders from what the APIs answer, with the user's own token.
import { forgetBackend, resolveBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";
import type { Availability } from "./availability";
import type { Me, OfferingPrivate, PractitionerPrivate, Taxonomy } from "./api";

export type Loaded<T> = { state: "ready"; value: T } | { state: "none" } | { state: "unavailable" };

async function get(backend: "directory" | "booking", path: string, token: string): Promise<Response | null> {
  const base = await resolveBackend(backend);
  if (!base) return null;
  try {
    return await backendFetch(base + path, { headers: { Accept: "application/json", Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(5_000), cache: "no-store" });
  } catch {
    forgetBackend(backend);
    return null;
  }
}

async function load<T>(backend: "directory" | "booking", path: string, token: string, pick: (body: unknown) => T): Promise<Loaded<T>> {
  const res = await get(backend, path, token);
  if (!res) return { state: "unavailable" };
  if (res.status === 404 || res.status === 403) return { state: "none" }; // no practitioner profile yet (Directory 404, Booking 403)
  if (!res.ok) return { state: "unavailable" };
  return { state: "ready", value: pick(await res.json().catch(() => null)) };
}

export const loadMe = (token: string) => load<Me>("directory", "/api/v1/me", token, (b) => b as Me);
export const loadProfile = (token: string) => load<PractitionerPrivate>("directory", "/api/v1/me/practitioner", token, (b) => b as PractitionerPrivate);
export const loadOfferings = (token: string) =>
  load<OfferingPrivate[]>("directory", "/api/v1/me/practitioner/offerings", token, (b) => ((b as { items?: OfferingPrivate[] } | null)?.items ?? []));
export const loadAvailability = (token: string) => load<Availability>("booking", "/api/v1/me/availability", token, (b) => b as Availability);
export const loadTaxonomy = (token: string) => load<Taxonomy>("directory", "/api/v1/taxonomy", token, (b) => b as Taxonomy);
