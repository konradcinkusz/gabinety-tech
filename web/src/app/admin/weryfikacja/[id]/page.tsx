import { notFound } from "next/navigation";
import { PractitionerDetail } from "@/components/admin/verification/PractitionerDetail";
import { idOf, loadPractitioner, requireAdmin } from "@/components/admin/verification/server";
import { av } from "@/components/admin/verification/strings";
import { Alert } from "@/components/ui";

export const dynamic = "force-dynamic";
export const metadata = { title: av("detail.title"), robots: { index: false, follow: false } };

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const raw = (await params).id;
  const gate = await requireAdmin(`/admin/weryfikacja/${encodeURIComponent(raw)}`); // the page checks the role itself, as the queue does
  if (!gate.ok) return <Alert variant="danger" data-testid="forbidden">{av("forbidden")}</Alert>;
  const id = idOf(raw);
  if (!id) notFound();
  const out = await loadPractitioner(gate.token, id);
  if (out.state === "forbidden") return <Alert variant="danger" data-testid="forbidden">{av("forbidden")}</Alert>;
  if (out.state === "notFound") notFound();
  if (out.state === "unavailable") return <Alert variant="danger" data-testid="unavailable">{av("detail.unavailable")}</Alert>;
  return <PractitionerDetail detail={out.detail} events={out.events} />;
}
