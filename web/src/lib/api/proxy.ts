import { readConfig } from "../config";
import { ACCESS_COOKIE, REFRESH_COOKIE, clearSessionCookies, parseCookies, setSessionCookies, type TokenSet } from "../session/cookies";
import { refreshOnce } from "../session/refresh";
import { forgetBackend, resolveBackend } from "./backends";
import { backendFetch } from "./http";
import { checkOrigin } from "./originGuard";
import { dependencyUnavailable, problem } from "./problem";

export const BACKENDS = ["directory", "booking", "reputation", "assistant"] as const;
const MAX_BODY = 1024 * 1024;
const FORWARD_REQUEST = ["idempotency-key", "content-type", "accept", "accept-language", "if-none-match"];
const FORWARD_RESPONSE = ["content-type", "etag", "cache-control", "retry-after", "location", "content-language", "vary"];
const PREFIX = "/api/proxy/";

/** SPEC 5.1 timeouts: reads 5 s, writes 10 s, POST /bookings 15 s, assistant brief 35 s. */
export function timeoutFor(method: string, path: string): number {
  const m = method.toUpperCase();
  if (m === "GET" || m === "HEAD") return 5_000;
  if (m === "POST" && path.includes("/api/v1/assistant/brief")) return 35_000;
  if (m === "POST" && path.includes("/api/v1/bookings")) return 15_000;
  return 10_000;
}

/** Rejects empty, dot, encoded-dot, encoded-slash and double-encoded segments. */
export function safeSegments(segments: string[]): boolean {
  for (const raw of segments) {
    if (raw === "") return false; // double slash, or a trailing slash with nothing behind it
    let decoded: string;
    try {
      decoded = decodeURIComponent(raw);
    } catch {
      return false;
    }
    if (decoded !== raw && /%/.test(decoded)) return false; // double encoding
    if (decoded === "." || decoded === ".." || /[\\/\u0000-\u001f]/.test(decoded)) return false;
  }
  return true;
}

async function readLimited(request: Request): Promise<Uint8Array | null> {
  const declared = Number(request.headers.get("content-length") ?? 0);
  if (declared > MAX_BODY) return null;
  if (!request.body) return new Uint8Array();
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_BODY) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }
  const out = new Uint8Array(size);
  let o = 0;
  for (const c of chunks) {
    out.set(c, o);
    o += c.byteLength;
  }
  return out;
}

/**
 * `matched` are the segments Next matched for [...path]. `new URL` already collapses `..` in
 * request.url, so both views are checked: the raw pathname and the matched segments.
 */
export async function handleProxy(request: Request, matched: string[] = []): Promise<Response> {
  const config = readConfig();
  const denied = checkOrigin(request, config.siteBaseUrl);
  if (denied) return denied;

  const url = new URL(request.url);
  const segments = url.pathname.startsWith(PREFIX) ? url.pathname.slice(PREFIX.length).split("/") : null;
  if (!segments || !safeSegments(segments) || !safeSegments(matched)) return problem(400, "invalid_path");
  const [backend, ...rest] = segments;
  if (!(BACKENDS as readonly string[]).includes(backend)) return problem(404, "unknown_backend");
  if (rest.length === 0) return problem(400, "invalid_path");

  const method = request.method.toUpperCase();
  const idempotent = method === "GET" || method === "HEAD";
  let payload: Uint8Array | undefined;
  if (!idempotent) {
    const b = await readLimited(request);
    if (b === null) return problem(413, "payload_too_large");
    payload = b;
  }

  const base = await resolveBackend(backend);
  if (!base) return dependencyUnavailable();
  const targetPath = "/" + rest.join("/");
  const target = base + targetPath + url.search;

  const cookies = parseCookies(request.headers.get("cookie")); // read here, never forwarded
  let access = cookies[ACCESS_COOKIE];
  const refreshToken = cookies[REFRESH_COOKIE];
  let renewed: TokenSet | null = null;
  let refreshFailed = false;

  const renew = async () => {
    if (!refreshToken) return false;
    renewed = await refreshOnce(refreshToken);
    if (!renewed) {
      refreshFailed = true;
      return false;
    }
    access = renewed.accessToken;
    return true;
  };

  const send = async (): Promise<Response> => {
    const headers = new Headers();
    for (const h of FORWARD_REQUEST) {
      const v = request.headers.get(h);
      if (v !== null) headers.set(h, v);
    }
    if (access) headers.set("authorization", `Bearer ${access}`);
    return backendFetch(target, {
      method,
      headers,
      body: payload && payload.byteLength > 0 ? (payload as unknown as BodyInit) : undefined,
      signal: AbortSignal.timeout(timeoutFor(method, targetPath)),
      redirect: "manual",
    });
  };

  try {
    if (!access) await renew(); // access cookie expired with its token, refresh cookie remains
    let upstream = await send();
    if (upstream.status === 401 && access && !renewed && !refreshFailed && (await renew()) && idempotent) {
      await upstream.body?.cancel();
      upstream = await send();
    }
    const out = new Response(method === "HEAD" ? null : upstream.body, {
      status: upstream.status,
      headers: pick(upstream.headers),
    });
    const site = config.siteBaseUrl;
    if (renewed) setSessionCookies(out, renewed, site);
    else if (refreshFailed) clearSessionCookies(out, site);
    return out;
  } catch {
    forgetBackend(backend);
    return dependencyUnavailable(); // never echo the error: it can carry the backend address
  }
}

function pick(from: Headers): Headers {
  const h = new Headers();
  for (const k of FORWARD_RESPONSE) {
    const v = from.get(k);
    if (v !== null) h.set(k, v);
  }
  return h; // Set-Cookie is deliberately not on the list
}
