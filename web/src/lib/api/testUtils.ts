import { SignJWT, exportJWK, generateKeyPair, type JWK } from "jose";
import { setBackendFetch } from "./http";

export type Call = { url: string; init: RequestInit; headers: Headers };

/** Installs a fake backend fetch that records calls and answers through `handler`. */
export function fakeBackend(handler: (call: Call) => Response | Promise<Response>) {
  const calls: Call[] = [];
  setBackendFetch(async (input, init = {}) => {
    const call = { url: String(input), init, headers: new Headers(init.headers) };
    calls.push(call);
    return handler(call);
  });
  return calls;
}

export const tokenBody = (access = "at-1", refresh = "rt-1") =>
  Response.json({ accessToken: access, refreshToken: refresh, expiresIn: 3600, tokenType: "Bearer" });

export async function makeKey(kid = "k1") {
  const { publicKey, privateKey } = await generateKeyPair("RS256", { extractable: true });
  const jwk: JWK = { ...(await exportJWK(publicKey)), kid, alg: "RS256", use: "sig" };
  return { privateKey, jwk, kid };
}

export async function signToken(
  key: Awaited<ReturnType<typeof makeKey>>,
  claims: Record<string, unknown> = {},
  opts: { iss?: string; aud?: string; exp?: string | number } = {},
) {
  return new SignJWT({ email: "demo-user@example.test", ...claims })
    .setProtectedHeader({ alg: "RS256", kid: key.kid })
    .setSubject("demo-user-1")
    .setIssuer(opts.iss ?? "AuthService")
    .setAudience(opts.aud ?? "AuthService")
    .setJti("jti-1")
    .setExpirationTime(opts.exp ?? "1h")
    .sign(key.privateKey);
}

export function cookieHeader(c: Record<string, string>) {
  return Object.entries(c).map(([k, v]) => `${k}=${v}`).join("; ");
}
