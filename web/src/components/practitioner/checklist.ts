import type { OfferingPrivate, PractitionerPrivate } from "./api";

export type ChecklistKey = "headline" | "bio" | "offering" | "github" | "nip" | "portfolio" | "email";
export type ChecklistItem = { key: ChecklistKey; done: boolean };

export const BIO_MIN = 200;

/** The submit checklist of CONTRACTS 3.5, in its order. Reads only what the API returned (profile and offerings), never form state. */
export function buildChecklist(p: PractitionerPrivate, offerings: readonly OfferingPrivate[]): ChecklistItem[] {
  return [
    { key: "headline", done: p.headline.trim().length > 0 },
    { key: "bio", done: p.bio.trim().length >= BIO_MIN },
    { key: "offering", done: offerings.some((o) => o.isActive) },
    { key: "github", done: p.githubLinked },
    { key: "nip", done: p.nipValid },
    { key: "portfolio", done: !!p.portfolioUrl },
    { key: "email", done: p.emailVerified },
  ];
}
