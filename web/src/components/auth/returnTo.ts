const AUTH_PAGES = ["/logowanie", "/rejestracja"];

/** A return target is accepted only as a same-site page path; anything else becomes "/". */
export function safeReturnTo(raw: string | null | undefined): string {
  if (!raw || !raw.startsWith("/") || raw.startsWith("//")) return "/";
  if (/[\u0000-\u001f\\]/.test(raw)) return "/";
  let u: URL;
  try {
    u = new URL(raw, "http://x.invalid");
  } catch {
    return "/";
  }
  if (u.origin !== "http://x.invalid") return "/";
  if (u.pathname === "/api" || u.pathname.startsWith("/api/")) return "/";
  if (AUTH_PAGES.some((p) => u.pathname === p || u.pathname.startsWith(p + "/"))) return "/";
  return raw;
}
