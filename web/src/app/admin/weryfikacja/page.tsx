import Link from "next/link";
import { VerificationQueue } from "@/components/admin/verification/VerificationQueue";
import { QUEUE_STATUSES } from "@/components/admin/verification/api";
import { loadQueue, requireAdmin, statusOf } from "@/components/admin/verification/server";
import { av, type AvKey } from "@/components/admin/verification/strings";
import { Alert } from "@/components/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: av("title"), robots: { index: false, follow: false } };

export default async function Page({ searchParams }: { searchParams: Promise<{ status?: string; cursor?: string }> }) {
  const sp = await searchParams;
  const gate = await requireAdmin("/admin/weryfikacja"); // a layout is not re-run on every navigation: the page checks too
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{av("forbidden")}</Alert>;
  const status = statusOf(sp.status);
  const queue = await loadQueue(gate.token, status, typeof sp.cursor === "string" ? sp.cursor : undefined);
  if (queue.state === "forbidden") return <Alert variant="danger" data-testid="forbidden">{av("forbidden")}</Alert>;
  return (
    <>
      <h1>{av("title")}</h1>
      <nav aria-label={av("filter.label")}>
        {QUEUE_STATUSES.map((s) => (
          <Link key={s} href={`/admin/weryfikacja?status=${s}`} aria-current={s === status ? "page" : undefined}>
            {av(`filter.${s}` as AvKey)}
          </Link>
        ))}
      </nav>
      {queue.state === "unavailable" ? (
        <Alert variant="danger">{av("unavailable")}</Alert>
      ) : (
        <>
          <VerificationQueue items={queue.page.items} />
          {queue.page.nextCursor ? <Link href={`/admin/weryfikacja?status=${status}&cursor=${encodeURIComponent(queue.page.nextCursor)}`}>{av("next")}</Link> : null}
        </>
      )}
    </>
  );
}
