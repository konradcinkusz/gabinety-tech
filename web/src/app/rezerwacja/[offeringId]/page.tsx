import { notFound } from "next/navigation";
import { BookingForm } from "@/components/booking/BookingForm";
import { bk } from "@/components/booking/strings";
import { getProfile } from "@/components/profile/profileApi";
import { LinkButton } from "@/components/ui";

export const dynamic = "force-dynamic"; // signed-in page, never cached
export const metadata = { title: bk("page.metaTitle"), robots: { index: false, follow: true } };

type Props = { params: Promise<{ offeringId: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> };
const one = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);

/**
 * `start` is the slot (a UTC instant). `gabinet` is the profile slug: there is no endpoint that resolves an offering id on its own,
 * so the offering is looked up in the public profile (see the PR: the slot links must carry `gabinet`).
 */
export default async function Page({ params, searchParams }: Props) {
  const [{ offeringId }, sp] = await Promise.all([params, searchParams]);
  const start = one(sp.start);
  const slug = one(sp.gabinet);
  if (!slug) return <Missing />;
  const profile = await getProfile(slug); // 404 for an unknown or unapproved profile
  const offering = profile.offerings.find((o) => o.id === offeringId);
  if (!offering) notFound();
  if (!start || Number.isNaN(Date.parse(start))) return <Missing slug={profile.slug} />;
  return (
    <>
      <h1>{bk("page.title")}</h1>
      <BookingForm
        offering={{ id: offering.id, title: offering.title, durationMinutes: offering.durationMinutes, price: offering.price, problemSlug: offering.problemSlug }}
        practitioner={{ slug: profile.slug, displayName: profile.displayName }}
        startUtc={new Date(start).toISOString()}
      />
    </>
  );
}

function Missing({ slug }: { slug?: string }) {
  return (
    <>
      <h1>{bk("page.title")}</h1>
      <p>{bk("page.missingSlot")}</p>
      <LinkButton href={slug ? `/gabinet/${slug}` : "/programisci"}>{bk(slug ? "error.toProfile" : "page.backToSearch")}</LinkButton>
    </>
  );
}
