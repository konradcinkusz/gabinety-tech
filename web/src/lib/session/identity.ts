import { resolveBackend, forgetBackend } from "../api/backends";
import { backendFetch } from "../api/http";
import type { TokenSet } from "./cookies";

export type IdentityResult =
  | { kind: "tokens"; tokens: TokenSet }
  | { kind: "accepted" }
  | { kind: "two_factor" }
  | { kind: "verification_required" }
  | { kind: "unauthorized" }
  | { kind: "rejected"; status: number }
  | { kind: "unavailable" };

const TIMEOUT_MS = 10_000;

async function call(path: string, init: { method: string; body?: unknown; bearer?: string }): Promise<Response | null> {
  const base = await resolveBackend("auth");
  if (!base) return null;
  const headers: Record<string, string> = { Accept: "application/json" };
  if (init.body !== undefined) headers["Content-Type"] = "application/json";
  if (init.bearer) headers["Authorization"] = `Bearer ${init.bearer}`;
  try {
    return await backendFetch(base + path, {
      method: init.method,
      headers,
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch {
    forgetBackend("auth");
    return null;
  }
}

async function readTokens(res: Response): Promise<IdentityResult> {
  const b = (await res.json().catch(() => null)) as Record<string, unknown> | null;
  if (b?.requiresTwoFactor) return { kind: "two_factor" };
  if (typeof b?.accessToken === "string" && typeof b.refreshToken === "string") {
    return {
      kind: "tokens",
      tokens: { accessToken: b.accessToken, refreshToken: b.refreshToken, expiresIn: Number(b.expiresIn) || 3600 },
    };
  }
  return { kind: "unavailable" };
}

async function classify(res: Response | null): Promise<IdentityResult> {
  if (!res) return { kind: "unavailable" };
  if (res.status === 200) return readTokens(res);
  if (res.status === 202) return { kind: "accepted" };
  if (res.status === 401) return { kind: "unauthorized" };
  if (res.status === 403) {
    const b = (await res.json().catch(() => null)) as { emailVerificationRequired?: boolean } | null;
    if (b?.emailVerificationRequired) return { kind: "verification_required" };
    return { kind: "rejected", status: 403 };
  }
  if (res.status >= 400 && res.status < 500) return { kind: "rejected", status: res.status };
  return { kind: "unavailable" };
}

export const identity = {
  login: async (email: string, password: string) =>
    classify(await call("/api/v1/auth/login", { method: "POST", body: { email, password } })),
  register: async (body: Record<string, unknown>) =>
    classify(await call("/api/v1/auth/register", { method: "POST", body })),
  refresh: async (refreshToken: string) =>
    classify(await call("/api/v1/auth/refresh", { method: "POST", body: { refreshToken } })),
  exchange: async (code: string) =>
    classify(await call("/api/v1/external-auth/exchange", { method: "POST", body: { code } })),
  logout: async (accessToken: string) => {
    const res = await call("/api/v1/auth/logout", { method: "POST", bearer: accessToken });
    return res !== null && res.ok;
  },
  consentVersions: async (): Promise<{ terms: string; privacy: string } | null> => {
    const res = await call("/api/v1/auth/consents/versions", { method: "GET" });
    if (!res?.ok) return null;
    const b = (await res.json().catch(() => null)) as { terms?: string; privacy?: string } | null;
    return b?.terms && b.privacy ? { terms: b.terms, privacy: b.privacy } : null;
  },
};
