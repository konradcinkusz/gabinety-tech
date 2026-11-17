// Shapes of the Directory and Booking user APIs (CONTRACTS 3.3, 4.2). The generated types of T-07.01 only know /api/v1/me/practitioner
// (request bodies, no responses), so the rest is declared here by hand, from src/Gabinety.Contracts. Do not edit web/src/lib/api/generated.
// The browser talks to its own origin only: every call goes through the BFF catch-all (CONTRACTS 7).
import type { Availability } from "./availability";

export type PractitionerStatus = "Draft" | "Submitted" | "Approved" | "Rejected" | "Suspended";
export type Me = { userId: string; practitionerId?: string; practitionerStatus?: PractitionerStatus; isAdmin: boolean; isModerator: boolean };
export type PractitionerPrivate = {
  id: string;
  slug: string;
  status: PractitionerStatus;
  statusReason?: string | null;
  displayName: string;
  headline: string;
  bio: string;
  portfolioUrl?: string | null;
  meetingUrlDefault?: string | null;
  nip?: string | null;
  emailVerified: boolean;
  githubLinked: boolean;
  nipValid: boolean;
  githubLogin?: string | null;
};
export type ProfileInput = { displayName: string; headline: string; bio: string; portfolioUrl?: string; meetingUrlDefault?: string; nip?: string };
export type OfferingPrivate = {
  id: string;
  title: string;
  description: string;
  durationMinutes: 30 | 60;
  price: { amountMinor: number; currency: "PLN" };
  problemSlug: string;
  techSlugs: string[];
  isActive: boolean;
  meetingUrl?: string | null;
};
export type OfferingInput = { title: string; description: string; durationMinutes: 30 | 60; priceMinor: number; problemSlug: string; techSlugs: string[]; meetingUrl?: string };
export type Category = { slug: string; namePl: string };
export type Taxonomy = { version: string; problems: Category[]; techs: Category[] };
export type GithubChallenge = { code: string; instruction: string };

/** An RFC 9457 answer reduced to what the UI needs: never the title, the detail or any free text. */
export type Problem = { status: number; code: string; errors: Record<string, string[]>; unmetItems: string[] };
export type Result<T> = { ok: true; value: T } | { ok: false; problem: Problem };

export interface PractitionerApi {
  getProfile(): Promise<Result<PractitionerPrivate>>;
  createProfile(displayName: string): Promise<Result<PractitionerPrivate>>;
  saveProfile(input: ProfileInput): Promise<Result<PractitionerPrivate>>;
  submit(): Promise<Result<void>>;
  requestGithubLink(githubLogin: string): Promise<Result<GithubChallenge>>;
  verifyGithubLink(): Promise<Result<void>>;
  listOfferings(): Promise<Result<OfferingPrivate[]>>;
  saveOffering(id: string | null, input: OfferingInput): Promise<Result<OfferingPrivate>>;
  deactivateOffering(id: string): Promise<Result<void>>;
  getAvailability(): Promise<Result<Availability>>;
  saveAvailability(a: Availability): Promise<Result<Availability>>;
  getTaxonomy(): Promise<Result<Taxonomy>>;
}

const DIR = "/api/proxy/directory/api/v1";
const BOOKING = "/api/proxy/booking/api/v1";
type Fetch = typeof fetch;

const asRecord = (v: unknown): Record<string, unknown> => (v && typeof v === "object" ? (v as Record<string, unknown>) : {});

export async function toProblem(res: Response): Promise<Problem> {
  const body = asRecord(await res.json().catch(() => null));
  const errors: Record<string, string[]> = {};
  for (const [k, v] of Object.entries(asRecord(body.errors))) errors[k] = Array.isArray(v) ? v.filter((m): m is string => typeof m === "string") : [];
  const unmet = Array.isArray(body.unmetItems) ? body.unmetItems.filter((m): m is string => typeof m === "string") : [];
  return { status: res.status, code: typeof body.code === "string" ? body.code : "unknown", errors, unmetItems: unmet };
}

export function createApi(doFetch: Fetch = (...a) => fetch(...a)): PractitionerApi {
  async function call<T>(method: string, url: string, body?: unknown): Promise<Result<T>> {
    let res: Response;
    try {
      res = await doFetch(url, {
        method,
        headers: body === undefined ? { Accept: "application/json" } : { Accept: "application/json", "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
        cache: "no-store",
      });
    } catch {
      return { ok: false, problem: { status: 0, code: "network", errors: {}, unmetItems: [] } };
    }
    if (!res.ok) return { ok: false, problem: await toProblem(res) };
    return { ok: true, value: (res.status === 204 ? undefined : await res.json().catch(() => undefined)) as T };
  }
  const items = async (r: Promise<Result<{ items: OfferingPrivate[] }>>): Promise<Result<OfferingPrivate[]>> => {
    const x = await r;
    return x.ok ? { ok: true, value: x.value.items } : x;
  };
  return {
    getProfile: () => call("GET", `${DIR}/me/practitioner`),
    createProfile: (displayName) => call("POST", `${DIR}/me/practitioner`, { displayName }),
    saveProfile: (input) => call("PUT", `${DIR}/me/practitioner`, input),
    submit: () => call("POST", `${DIR}/me/practitioner/submit`),
    requestGithubLink: (githubLogin) => call("POST", `${DIR}/me/practitioner/github-link`, { githubLogin }),
    verifyGithubLink: () => call("POST", `${DIR}/me/practitioner/github-link/verify`),
    listOfferings: () => items(call("GET", `${DIR}/me/practitioner/offerings`)),
    saveOffering: (id, input) => call(id ? "PUT" : "POST", `${DIR}/me/practitioner/offerings${id ? "/" + encodeURIComponent(id) : ""}`, input),
    deactivateOffering: (id) => call("POST", `${DIR}/me/practitioner/offerings/${encodeURIComponent(id)}/deactivate`),
    getAvailability: () => call("GET", `${BOOKING}/me/availability`),
    saveAvailability: (a) => call("PUT", `${BOOKING}/me/availability`, a),
    getTaxonomy: () => call("GET", `${DIR}/taxonomy`),
  };
}
