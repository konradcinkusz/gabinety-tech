import Link from "next/link";
import { LoginForm } from "@/components/auth/LoginForm";
import { authPath, RETURN_PARAM, safeReturnTo } from "@/components/auth/returnTo";
import { at } from "@/components/auth/strings";

export const metadata = { title: at("login.title") };
type Search = Promise<Record<string, string | string[] | undefined>>;
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);

// `redirect` is the one return parameter (RETURN_PARAM): the edge guard and the BFF use it too. The target is filtered.
export default async function LoginPage({ searchParams }: { searchParams: Search }) {
  const sp = await searchParams;
  const back = safeReturnTo(one(sp[RETURN_PARAM]));
  const error = one(sp.error);
  const notice = error === "github_failed" ? at("login.githubFailed") : error === "two_factor_not_supported" ? at("login.twoFactor") : undefined;
  return (
    <>
      <h1>{at("login.title")}</h1>
      <LoginForm returnTo={back} notice={notice} />
      <p>
        {at("login.noAccount")} <Link href={authPath("/rejestracja", back)}>{at("register.title")}</Link>
      </p>
    </>
  );
}
