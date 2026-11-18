// Next 16 renamed the `middleware` file convention to `proxy` (see node_modules/next/dist/docs,
// file-conventions/proxy.md). This is the "edge middleware that verifies" of ADR-0001.
import { NextResponse, type NextRequest } from "next/server";
import { resolveBackend } from "./lib/api/backends";
import { readConfig } from "./lib/config";
import { buildCsp } from "./lib/security/headers";
import { decide, remoteVerifier, type Verifier } from "./lib/session/guard";

export async function proxy(request: NextRequest) {
  const { pathname, search } = request.nextUrl;
  const verify: Verifier = async (token) => {
    const base = await resolveBackend("auth");
    if (!base) throw new Error("identity not configured");
    const { issuer, audience } = readConfig().identity;
    return remoteVerifier(base, { issuer, audience })(token);
  };
  const d = await decide({ pathname, search, accessToken: request.cookies.get("gt_at")?.value, refreshToken: request.cookies.get("gt_rt")?.value }, verify);
  const nonce = Buffer.from(crypto.randomUUID()).toString("base64");
  const csp = buildCsp(nonce, process.env.NODE_ENV === "development");
  let response: NextResponse;
  if (d.action === "next") {
    // Next reads the nonce from the request's CSP header and stamps its own inline scripts with it.
    const requestHeaders = new Headers(request.headers);
    requestHeaders.set("x-nonce", nonce);
    requestHeaders.set("Content-Security-Policy", csp);
    response = NextResponse.next({ request: { headers: requestHeaders } });
  } else {
    response = NextResponse.redirect(new URL(d.to, request.url));
  }
  response.headers.set("Content-Security-Policy", csp);
  return response;
}

export const config = {
  matcher: ["/((?!api|_next/static|_next/image|favicon.ico|.*\\.[a-zA-Z0-9]+$).*)"],
};
