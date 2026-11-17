import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { AccountView } from "@/components/auth/AccountView";
import { loadAccount } from "@/components/auth/account.server";
import { at } from "@/components/auth/strings";
import { Alert } from "@/components/ui";
import { ACCESS_COOKIE } from "@/lib/session/cookies";
import { acceptAction, removeAction } from "./actions";

export const dynamic = "force-dynamic";
export const metadata = { title: at("account.title") };

export default async function AccountPage() {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) redirect("/logowanie?redirect=%2Fkonto");
  const account = await loadAccount(token);
  if (!account) return <Alert variant="danger">{at("account.unavailable")}</Alert>;
  return <AccountView account={account} actions={{ accept: acceptAction, remove: removeAction }} />;
}
