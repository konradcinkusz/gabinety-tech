import Link from "next/link";
import { DisputeList } from "@/components/admin/disputes/DisputeList";
import { DISPUTE_STATUSES } from "@/components/admin/disputes/api";
import { cursorOf, loadDisputes, statusOf } from "@/components/admin/disputes/server";
import { dv, type DvKey } from "@/components/admin/disputes/strings";
import { requireAdmin } from "@/components/admin/verification/server";
import { Alert } from "@/components/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: dv("title"), robots: { index: false, follow: false } };

export default async function Page({ searchParams }: { searchParams: Promise<{ status?: string; cursor?: string }> }) {
  const sp = await searchParams;
  const gate = await requireAdmin("/admin/spory"); // a layout is not re-run on every navigation: the page checks too
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{dv("forbidden")}</Alert>;
  const status = statusOf(sp.status);
  const result = await loadDisputes(gate.token, status, cursorOf(sp.cursor));
  if (result.state === "forbidden") return <Alert variant="danger" data-testid="forbidden">{dv("forbidden")}</Alert>;
  return (
    <>
      <h1>{dv("title")}</h1>
      <nav aria-label={dv("filter.label")}>
        {DISPUTE_STATUSES.map((s) => (
          <Link key={s} href={`/admin/spory?status=${s}`} aria-current={s === status ? "page" : undefined}>
            {dv(`filter.${s}` as DvKey)}
          </Link>
        ))}
      </nav>
      {result.state === "unavailable" ? (
        <Alert variant="danger">{dv("unavailable")}</Alert>
      ) : (
        <>
          <DisputeList items={result.page.items} mode={status} />
          {result.page.nextCursor ? <Link href={`/admin/spory?status=${status}&cursor=${encodeURIComponent(result.page.nextCursor)}`}>{dv("next")}</Link> : null}
        </>
      )}
    </>
  );
}
