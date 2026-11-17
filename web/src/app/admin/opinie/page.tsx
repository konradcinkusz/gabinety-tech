import Link from "next/link";
import { ReviewList } from "@/components/admin/reviews/ReviewList";
import { REVIEW_STATUSES } from "@/components/admin/reviews/api";
import { cursorOf, loadReviews, statusOf } from "@/components/admin/reviews/server";
import { mv, type MvKey } from "@/components/admin/reviews/strings";
import { requireAdmin } from "@/components/admin/verification/server";
import { Alert } from "@/components/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: mv("title"), robots: { index: false, follow: false } };

export default async function Page({ searchParams }: { searchParams: Promise<{ status?: string; cursor?: string }> }) {
  const sp = await searchParams;
  const gate = await requireAdmin("/admin/opinie"); // a layout is not re-run on every navigation: the page checks too
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{mv("forbidden")}</Alert>;
  const status = statusOf(sp.status);
  const result = await loadReviews(gate.token, status, cursorOf(sp.cursor));
  if (result.state === "forbidden") return <Alert variant="danger" data-testid="forbidden">{mv("forbidden")}</Alert>;
  return (
    <>
      <h1>{mv("title")}</h1>
      <nav aria-label={mv("filter.label")}>
        {REVIEW_STATUSES.map((s) => (
          <Link key={s} href={`/admin/opinie?status=${s}`} aria-current={s === status ? "page" : undefined}>
            {mv(`filter.${s}` as MvKey)}
          </Link>
        ))}
      </nav>
      {result.state === "unavailable" ? (
        <Alert variant="danger" data-testid="unavailable">{mv("unavailable")}</Alert>
      ) : (
        <>
          <ReviewList items={result.page.items} mode={status} />
          {result.page.nextCursor ? <Link href={`/admin/opinie?status=${status}&cursor=${encodeURIComponent(result.page.nextCursor)}`}>{mv("next")}</Link> : null}
        </>
      )}
    </>
  );
}
