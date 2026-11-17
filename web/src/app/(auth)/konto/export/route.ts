import { exportAccount } from "@/components/auth/account.server";
import { ACCESS_COOKIE, parseCookies } from "@/lib/session/cookies";

export const dynamic = "force-dynamic";

export async function GET(request: Request): Promise<Response> {
  const token = parseCookies(request.headers.get("cookie"))[ACCESS_COOKIE];
  if (!token) return new Response(null, { status: 401, headers: { "Cache-Control": "no-store" } });
  return exportAccount(token);
}
