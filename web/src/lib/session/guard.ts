import { jwtVerify, createRemoteJWKSet, type JWTPayload, type JWTVerifyGetKey } from "jose";
import { isAdminClaims } from "./claims";

export type Verifier = (token: string) => Promise<JWTPayload>;
export type DecideInput = { pathname: string; search: string; accessToken?: string };
export type Decision = { action: "next" } | { action: "redirect"; to: string };

const PROTECTED = ["/moje", "/gabinet-programisty", "/admin", "/rezerwacja", "/checkout", "/konto"];
const under = (path: string, prefix: string) => path === prefix || path.startsWith(prefix + "/");

/** Verifies a SIGNATURE (RS256), expiry (5 s tolerance) and, when configured, issuer and audience. */
export function createVerifier(jwks: JWTVerifyGetKey, opts: { issuer?: string; audience?: string } = {}): Verifier {
  return async (token) =>
    (await jwtVerify(token, jwks, { algorithms: ["RS256"], clockTolerance: 5, issuer: opts.issuer, audience: opts.audience })).payload;
}

const jwksCache = new Map<string, JWTVerifyGetKey>();
export function remoteVerifier(authBase: string, opts: { issuer?: string; audience?: string }): Verifier {
  const url = `${authBase}/.well-known/jwks.json`;
  let jwks = jwksCache.get(url);
  if (!jwks) {
    jwks = createRemoteJWKSet(new URL(url)); // caches keys, honours cooldown, refetches on unknown kid
    jwksCache.set(url, jwks);
  }
  return createVerifier(jwks, opts);
}

export async function decide(input: DecideInput, verify: Verifier): Promise<Decision> {
  const { pathname } = input;
  if (!PROTECTED.some((p) => under(pathname, p))) return { action: "next" };
  const login = { action: "redirect", to: "/logowanie?redirect=" + encodeURIComponent(pathname + input.search) } as const;
  if (!input.accessToken) return login;
  let claims: JWTPayload;
  try {
    claims = await verify(input.accessToken);
  } catch {
    return login;
  }
  if (under(pathname, "/admin") && !isAdminClaims(claims)) return { action: "redirect", to: "/" };
  return { action: "next" };
}
