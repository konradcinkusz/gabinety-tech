export const ACCESS_COOKIE = "gt_at";
export const REFRESH_COOKIE = "gt_rt";
export const REFRESH_MAX_AGE = 7 * 24 * 3600;

export type TokenSet = { accessToken: string; refreshToken: string; expiresIn: number };

export function parseCookies(header: string | null): Record<string, string> {
  const out: Record<string, string> = {};
  for (const part of (header ?? "").split(";")) {
    const i = part.indexOf("=");
    if (i < 0) continue;
    const k = part.slice(0, i).trim();
    if (k && !(k in out)) out[k] = part.slice(i + 1).trim();
  }
  return out;
}

// Host-only (no Domain), HttpOnly, SameSite=Lax, Secure when the site is served over https.
function serialize(name: string, value: string, maxAge: number, secure: boolean): string {
  const attrs = [`${name}=${value}`, "Path=/", `Max-Age=${maxAge}`, "HttpOnly", "SameSite=Lax"];
  if (maxAge === 0) attrs.push("Expires=Thu, 01 Jan 1970 00:00:00 GMT");
  if (secure) attrs.push("Secure");
  return attrs.join("; ");
}

export const isSecure = (siteBaseUrl: string) => siteBaseUrl.startsWith("https://");

export function setSessionCookies(res: Response, t: TokenSet, siteBaseUrl: string): Response {
  const secure = isSecure(siteBaseUrl);
  res.headers.append("Set-Cookie", serialize(ACCESS_COOKIE, t.accessToken, Math.max(1, Math.floor(t.expiresIn)), secure));
  res.headers.append("Set-Cookie", serialize(REFRESH_COOKIE, t.refreshToken, REFRESH_MAX_AGE, secure));
  return res;
}

export function clearSessionCookies(res: Response, siteBaseUrl: string): Response {
  const secure = isSecure(siteBaseUrl);
  res.headers.append("Set-Cookie", serialize(ACCESS_COOKIE, "", 0, secure));
  res.headers.append("Set-Cookie", serialize(REFRESH_COOKIE, "", 0, secure));
  return res;
}
