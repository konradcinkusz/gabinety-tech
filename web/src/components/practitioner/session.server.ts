import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { ACCESS_COOKIE } from "@/lib/session/cookies";

/** The edge guard already sends a signed-out visitor to /logowanie; this is the second check, at the page. */
export async function requireToken(path: string): Promise<string> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) redirect("/logowanie?redirect=" + encodeURIComponent(path));
  return token;
}
