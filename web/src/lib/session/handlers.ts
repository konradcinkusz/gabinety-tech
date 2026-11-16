import { checkOrigin } from "../api/originGuard";
import { dependencyUnavailable, problem } from "../api/problem";
import { resolveBackend } from "../api/backends";
import { readConfig } from "../config";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSessionCookies, parseCookies, setSessionCookies } from "./cookies";
import { readIdentity } from "./claims";
import { identity, type IdentityResult } from "./identity";
import { refreshOnce } from "./refresh";

const noStore = { "Cache-Control": "no-store" };
const ok = (body: unknown, status = 200) => Response.json(body, { status, headers: noStore });

async function body(request: Request): Promise<Record<string, unknown>> {
  try {
    const b = await request.json();
    return b && typeof b === "object" ? (b as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}
const str = (v: unknown) => (typeof v === "string" ? v : undefined);
const guard = (request: Request) => checkOrigin(request, readConfig().siteBaseUrl);

/** Maps an identity answer to a response; tokens become cookies, never body. */
function finish(r: IdentityResult, uniform: { status: number; code: string }): Response {
  const site = readConfig().siteBaseUrl;
  switch (r.kind) {
    case "tokens": {
      const who = readIdentity(r.tokens.accessToken);
      return setSessionCookies(ok({ authenticated: true, ...(who ?? {}) }), r.tokens, site);
    }
    case "accepted":
      return ok({ code: "verification_required" }, 202);
    case "two_factor":
      return problem(400, "two_factor_not_supported");
    case "verification_required":
      return problem(403, "email_verification_required");
    case "unauthorized":
    case "rejected":
      return problem(uniform.status, uniform.code);
    default:
      return dependencyUnavailable();
  }
}

export async function login(request: Request): Promise<Response> {
  const denied = guard(request);
  if (denied) return denied;
  const b = await body(request);
  const email = str(b.email);
  const password = str(b.password);
  if (!email || !password) return problem(400, "invalid_request");
  return finish(await identity.login(email, password), { status: 401, code: "invalid_credentials" });
}

export async function register(request: Request): Promise<Response> {
  const denied = guard(request);
  if (denied) return denied;
  const b = await body(request);
  const email = str(b.email);
  const password = str(b.password);
  if (!email || !password) return problem(400, "invalid_request");
  let terms = str(b.acceptedTermsVersion);
  let privacy = str(b.acceptedPrivacyVersion);
  if (!terms || !privacy) {
    const v = await identity.consentVersions();
    if (!v) return dependencyUnavailable();
    terms = terms ?? v.terms;
    privacy = privacy ?? v.privacy;
  }
  const result = await identity.register({
    email,
    password,
    acceptedTermsVersion: terms,
    acceptedPrivacyVersion: privacy,
    locale: str(b.locale),
  });
  return finish(result, { status: 400, code: "registration_failed" });
}

export async function logout(request: Request): Promise<Response> {
  const denied = guard(request);
  if (denied) return denied;
  const at = parseCookies(request.headers.get("cookie"))[ACCESS_COOKIE];
  if (at) await identity.logout(at); // best effort: the local session ends either way
  return clearSessionCookies(ok({ authenticated: false }), readConfig().siteBaseUrl);
}

export async function refresh(request: Request): Promise<Response> {
  const denied = guard(request);
  if (denied) return denied;
  const site = readConfig().siteBaseUrl;
  const rt = parseCookies(request.headers.get("cookie"))[REFRESH_COOKIE];
  if (!rt) return problem(401, "unauthenticated");
  const tokens = await refreshOnce(rt);
  if (!tokens) return clearSessionCookies(problem(401, "unauthenticated"), site);
  return setSessionCookies(ok({ authenticated: true }), tokens, site);
}

export function session(request: Request): Response {
  const who = readIdentity(parseCookies(request.headers.get("cookie"))[ACCESS_COOKIE]);
  return ok(who ? { authenticated: true, ...who } : { authenticated: false, userId: null, email: null, isAdmin: false });
}

/** A redirect target is accepted only as a same-site relative path. */
export function safeRedirect(target: string | null): string {
  if (!target || !target.startsWith("/") || target.startsWith("//") || target.startsWith("/\\")) return "/";
  if (/[\u0000-\u001f\\]/.test(target)) return "/";
  try {
    return new URL(target, "http://x.invalid").origin === "http://x.invalid" ? target : "/";
  } catch {
    return "/";
  }
}

export async function githubStart(request: Request): Promise<Response> {
  const config = readConfig();
  const base = config.identity.publicUrl ?? (await resolveBackend("auth"));
  if (!base) return dependencyUnavailable();
  const redirect = safeRedirect(new URL(request.url).searchParams.get("redirect"));
  // returnUrl comes from Site__BaseUrl, never from the request's own host.
  const returnUrl = new URL("/api/auth/github/callback", config.siteBaseUrl);
  if (redirect !== "/") returnUrl.searchParams.set("redirect", redirect);
  const target = new URL("/api/v1/external-auth/login", base);
  target.searchParams.set("provider", "GitHub");
  target.searchParams.set("returnUrl", returnUrl.toString());
  return new Response(null, { status: 302, headers: { Location: target.toString(), ...noStore } });
}

export async function githubCallback(request: Request): Promise<Response> {
  const config = readConfig();
  const url = new URL(request.url);
  const to = (path: string) => new Response(null, { status: 302, headers: { Location: config.siteBaseUrl + path, ...noStore } });
  const code = url.searchParams.get("code");
  if (!code) return to("/logowanie?error=github_failed");
  const r = await identity.exchange(code);
  if (r.kind === "two_factor") return to("/logowanie?error=two_factor_not_supported");
  if (r.kind !== "tokens") return to("/logowanie?error=github_failed");
  return setSessionCookies(to(safeRedirect(url.searchParams.get("redirect"))), r.tokens, config.siteBaseUrl);
}
