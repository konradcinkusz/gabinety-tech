import Link from "next/link";
import type { ReactNode } from "react";
import { Alert } from "@/components/ui";
import { requireAdmin } from "@/components/admin/verification/server";
import { av } from "@/components/admin/verification/strings";

// SPEC 6.5: noindex under /admin. Minimal shell of T-07.10a: the other admin pages add themselves under it (their links are here already).
export const metadata = { robots: { index: false, follow: false } };
export const dynamic = "force-dynamic";

export default async function AdminLayout({ children }: { children: ReactNode }) {
  const gate = await requireAdmin("/admin/weryfikacja");
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{av("forbidden")}</Alert>;
  return (
    <>
      <nav aria-label={av("nav.label")}>
        <Link href="/admin/weryfikacja">{av("nav.verification")}</Link> · <Link href="/admin/spory">{av("nav.disputes")}</Link> ·{" "}
        <Link href="/admin/ksiega">{av("nav.ledger")}</Link> · <Link href="/admin/opinie">{av("nav.reviews")}</Link>
      </nav>
      {children}
    </>
  );
}
