import { resolveBackend, forgetBackend } from "@/lib/api/backends";
import { backendFetch } from "@/lib/api/http";

/** What the account page may show. Built field by field from authservice's answers, so a token can never ride along. */
export type Account = {
  email: string;
  emailConfirmed: boolean;
  hasPassword: boolean;
  terms: { accepted: boolean; version: string };
  privacy: { accepted: boolean; version: string };
};

async function call(path: string, token: string, init: { method?: string; body?: unknown } = {}): Promise<Response | null> {
  const base = await resolveBackend("auth");
  if (!base) return null;
  try {
    return await backendFetch(base + path, {
      method: init.method ?? "GET",
      headers: {
        Accept: "application/json",
        Authorization: `Bearer ${token}`,
        ...(init.body === undefined ? {} : { "Content-Type": "application/json" }),
      },
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: AbortSignal.timeout(10_000),
    });
  } catch {
    forgetBackend("auth");
    return null;
  }
}

const rec = (v: unknown): Record<string, unknown> => (v && typeof v === "object" ? (v as Record<string, unknown>) : {});
const text = (v: unknown) => (typeof v === "string" ? v : "");

export async function loadAccount(token: string): Promise<Account | null> {
  const [me, consents] = await Promise.all([call("/api/v1/auth/me", token), call("/api/v1/auth/consents", token)]);
  if (!me?.ok || !consents?.ok) return null;
  const m = rec(await me.json().catch(() => null));
  const c = rec(await consents.json().catch(() => null));
  if (!text(m.email)) return null;
  const part = (v: unknown) => {
    const p = rec(v);
    return { accepted: p.accepted === true, version: text(p.accepted === true ? p.acceptedVersion : p.requiredVersion) };
  };
  return {
    email: text(m.email),
    emailConfirmed: m.emailConfirmed === true,
    hasPassword: m.hasPassword !== false,
    terms: part(c.terms),
    privacy: part(c.privacy),
  };
}

/** The user's own data as a downloadable file; the body is authservice's export, passed through. */
export async function exportAccount(token: string): Promise<Response> {
  const res = await call("/api/v1/auth/export", token);
  if (!res) return new Response(null, { status: 503, headers: { "Cache-Control": "no-store" } });
  if (!res.ok) return new Response(null, { status: res.status === 401 ? 401 : 502, headers: { "Cache-Control": "no-store" } });
  return new Response(res.body, {
    status: 200,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Content-Disposition": 'attachment; filename="gabinety-tech-moje-dane.json"',
      "Cache-Control": "no-store",
    },
  });
}

export async function acceptCurrentConsents(token: string): Promise<boolean> {
  const res = await call("/api/v1/auth/consents", token, { method: "POST", body: { acceptedTerms: true, acceptedPrivacy: true } });
  return res?.ok === true;
}

export async function deleteAccount(token: string, password?: string): Promise<"ok" | "rejected" | "unavailable"> {
  const body = { ...(password ? { password } : {}), confirmation: "DELETE" };
  const res = await call("/api/v1/auth/account", token, { method: "DELETE", body });
  if (!res) return "unavailable";
  if (res.ok) return "ok";
  return res.status >= 500 ? "unavailable" : "rejected";
}
