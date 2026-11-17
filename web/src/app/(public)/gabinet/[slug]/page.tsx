import type { Metadata } from "next";
import { ProfilePage } from "@/components/profile/ProfilePage";
import { getProfile } from "@/components/profile/profileApi";
import { pf } from "@/components/profile/strings";
import { readConfig } from "@/lib/config";

export const revalidate = 300; // 5 minutes (SPEC 6.5); Next requires a literal here
type Props = { params: Promise<{ slug: string }> };
const urlOf = (slug: string) => `${readConfig().siteBaseUrl}/gabinet/${encodeURIComponent(slug)}`;

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  const p = await getProfile(slug); // a 404 here is the page's 404
  const title = pf("page.metaTitle", { name: p.displayName });
  const description = pf("page.metaDescription", { name: p.displayName, headline: p.headline });
  const url = urlOf(p.slug);
  return {
    title,
    description,
    alternates: { canonical: url },
    robots: { index: true, follow: true },
    openGraph: { title, description, url, type: "profile", locale: "pl_PL" },
  };
}

export default async function Page({ params }: Props) {
  const { slug } = await params;
  const profile = await getProfile(slug);
  return <ProfilePage profile={profile} url={urlOf(profile.slug)} />;
}
