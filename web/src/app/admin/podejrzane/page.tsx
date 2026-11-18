import Link from "next/link";
import { PairList } from "@/components/admin/suspicious/PairList";
import { cursorOf, loadPairs } from "@/components/admin/suspicious/server";
import { sp } from "@/components/admin/suspicious/strings";
import { requireAdmin } from "@/components/admin/verification/server";
import { Alert } from "@/components/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: sp("title"), robots: { index: false, follow: false } };

export default async function Page({ searchParams }: { searchParams: Promise<{ cursor?: string }> }) {
  const params = await searchParams;
  const gate = await requireAdmin("/admin/podejrzane"); // a layout is not re-run on every navigation: the page checks too
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{sp("forbidden")}</Alert>;
  const result = await loadPairs(gate.token, cursorOf(params.cursor));
  if (result.state === "forbidden") return <Alert variant="danger" data-testid="forbidden">{sp("forbidden")}</Alert>;
  return (
    <>
      <h1>{sp("title")}</h1>
      <p>{sp("intro")}</p>
      {result.state === "unavailable" ? (
        <Alert variant="danger" data-testid="unavailable">{sp("unavailable")}</Alert>
      ) : (
        <>
          <PairList items={result.page.items} />
          {result.page.nextCursor ? <Link href={`/admin/podejrzane?cursor=${encodeURIComponent(result.page.nextCursor)}`}>{sp("next")}</Link> : null}
        </>
      )}
    </>
  );
}
