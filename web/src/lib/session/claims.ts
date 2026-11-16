import { decodeJwt } from "jose";

export const ROLE_CLAIM = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";

export const rolesOf = (claims: Record<string, unknown>): string[] => {
  const r = claims[ROLE_CLAIM] ?? claims["role"];
  return Array.isArray(r) ? r.map(String) : typeof r === "string" ? [r] : [];
};
export const isAdminClaims = (claims: Record<string, unknown>) =>
  rolesOf(claims).some((r) => r === "Admin" || r === "SuperAdmin");

/** UX-only decode of the cookie's access token (display, not authorization: the API verifies). */
export function readIdentity(accessToken: string | undefined) {
  if (!accessToken) return null;
  try {
    const c = decodeJwt(accessToken);
    if (typeof c.exp === "number" && c.exp * 1000 <= Date.now()) return null;
    return { userId: String(c.sub ?? ""), email: String(c["email"] ?? ""), isAdmin: isAdminClaims(c) };
  } catch {
    return null;
  }
}
