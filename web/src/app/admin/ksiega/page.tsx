import Link from "next/link";
import { csvHref, periodOf, presets } from "@/components/admin/ledger/api";
import { lg, type LgKey } from "@/components/admin/ledger/strings";
import { requireAdmin } from "@/components/admin/verification/server";
import { Alert } from "@/components/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: lg("title"), robots: { index: false, follow: false } };

export default async function Page({ searchParams }: { searchParams: Promise<{ from?: string; to?: string }> }) {
  const sp = await searchParams;
  const gate = await requireAdmin("/admin/ksiega"); // a layout is not re-run on every navigation: the page checks too
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{lg("forbidden")}</Alert>;
  const now = new Date();
  const period = periodOf(sp, now);
  return (
    <>
      <h1>{lg("title")}</h1>
      <p>{lg("intro")}</p>
      <nav aria-label={lg("preset.label")}>
        {presets(now).map((p) => (
          <Link key={p.id} href={`/admin/ksiega?${new URLSearchParams({ from: p.from, to: p.to })}`} aria-current={p.from === period.from && p.to === period.to ? "page" : undefined}>
            {lg(`preset.${p.id}` as LgKey)}
          </Link>
        ))}
      </nav>
      <form method="get" action="/admin/ksiega" aria-label={lg("filter.label")}>
        <label>
          {lg("filter.from")} <input type="date" name="from" defaultValue={period.from} required />
        </label>{" "}
        <label>
          {lg("filter.to")} <input type="date" name="to" defaultValue={period.to} required />
        </label>{" "}
        <button type="submit">{lg("filter.show")}</button>
      </form>
      {period.problem ? (
        <Alert variant="danger">{lg(`problem.${period.problem}` as LgKey)}</Alert>
      ) : (
        <>
          <p>{lg("summary", { from: period.from, to: period.to })}</p>
          <a href={csvHref(period.from, period.to)} download>{lg("download")}</a>
        </>
      )}
    </>
  );
}
