import { problem } from "./problem";

const STATE_CHANGING = new Set(["POST", "PUT", "PATCH", "DELETE"]);

/**
 * CSRF defence for cookie-authenticated state-changing requests. Returns a 403 problem response
 * for a cross-site request, or null when the request may proceed. GET is never guarded.
 */
export function checkOrigin(request: Request, siteBaseUrl: string): Response | null {
  if (!STATE_CHANGING.has(request.method.toUpperCase())) return null;
  if (request.headers.get("sec-fetch-site") === "cross-site") return deny();
  const origin = request.headers.get("origin");
  if (origin === null) return null;
  const own = new Set<string>();
  for (const base of [siteBaseUrl, request.url]) {
    try {
      own.add(new URL(base).origin);
    } catch {
      /* ignore an unparsable base */
    }
  }
  return own.has(origin) ? null : deny();
}

const deny = () => problem(403, "cross_site_request");
