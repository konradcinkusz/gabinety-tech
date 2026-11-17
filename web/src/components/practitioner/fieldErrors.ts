import type { Problem } from "./api";
import { pp, ppl, type PKey } from "./strings";

/**
 * The anti-leakage check lives on the server (CONTRACTS 3.5) and answers `contact_details_not_allowed` WITHOUT saying which field.
 * This only decides where to PUT the server's error: a loose look for an e-mail, a phone number, a messenger name or an @handle.
 * It never blocks a save and is not a security control.
 */
export function looksLikeContact(text: string): boolean {
  return /[^\s@]+@[^\s@]+/.test(text) || /(?:\d[\s().-]?){9,}/.test(text) || /\b(?:telegram|whatsapp|signal|discord|messenger|skype)\b/i.test(text) || /(?:^|\s)@\w{3,}/.test(text);
}

const FIELD_KEYS = new Set(Object.keys(ppl.fieldErrors));

/** Per-field messages from a Problem. `freeText` are the current values of the fields the contact-detail rule covers. */
export function fieldErrorsFrom(problem: Problem, freeText: Record<string, string>): Record<string, string> {
  const out: Record<string, string> = {};
  for (const key of Object.keys(problem.errors)) out[key] = pp(`fieldErrors.${FIELD_KEYS.has(key) ? key : "unknown"}` as PKey);
  if (problem.code === "contact_details_not_allowed") {
    const names = Object.keys(freeText);
    const hit = names.filter((n) => looksLikeContact(freeText[n]));
    for (const n of hit.length > 0 ? hit : names) out[n] = pp("fieldErrors.contact_details_not_allowed");
  }
  return out;
}

/** One line for the problems that belong to no field. */
export function generalMessage(problem: Problem, fallback: string): string {
  if (problem.status === 0) return pp("common.network");
  if (problem.status === 403) return pp("common.forbidden");
  if (problem.status === 503) return pp("common.unavailable");
  return fallback;
}
