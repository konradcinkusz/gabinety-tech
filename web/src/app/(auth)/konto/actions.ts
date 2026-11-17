"use server";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { acceptCurrentConsents, deleteAccount } from "@/components/auth/account.server";
import { ACCESS_COOKIE, REFRESH_COOKIE } from "@/lib/session/cookies";

export async function acceptAction(): Promise<void> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) redirect("/logowanie?redirect=%2Fkonto");
  await acceptCurrentConsents(token);
  redirect("/konto");
}

export async function removeAction(password: string): Promise<{ failed: boolean }> {
  const jar = await cookies();
  const token = jar.get(ACCESS_COOKIE)?.value;
  if (!token) redirect("/logowanie?redirect=%2Fkonto");
  if ((await deleteAccount(token, password || undefined)) !== "ok") return { failed: true };
  jar.delete(ACCESS_COOKIE);
  jar.delete(REFRESH_COOKIE);
  redirect("/");
}
