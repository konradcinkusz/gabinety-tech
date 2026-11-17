import Link from "next/link";
import { RegisterForm } from "@/components/auth/RegisterForm";
import { authPath, RETURN_PARAM, safeReturnTo } from "@/components/auth/returnTo";
import { at } from "@/components/auth/strings";
import { Alert } from "@/components/ui";
import { identity } from "@/lib/session/identity";

export const dynamic = "force-dynamic";
export const metadata = { title: at("register.title") };
type Search = Promise<Record<string, string | string[] | undefined>>;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);

export default async function RegisterPage({ searchParams }: { searchParams: Search }) {
  const sp = await searchParams;
  const back = safeReturnTo(one(sp[RETURN_PARAM]));
  const versions = await identity.consentVersions(); // read on the server, from the identity provider
  return (
    <>
      <h1>{at("register.title")}</h1>
      {versions ? <RegisterForm versions={versions} returnTo={back} /> : <Alert variant="danger">{at("register.unavailable")}</Alert>}
      <p>
        {at("register.haveAccount")} <Link href={authPath("/logowanie", back)}>{at("login.title")}</Link>
      </p>
    </>
  );
}
