import { Alert } from "@/components/ui";
import { CreateProfile } from "@/components/practitioner/CreateProfile";
import { OfferingsManager } from "@/components/practitioner/OfferingsManager";
import { loadOfferings, loadTaxonomy } from "@/components/practitioner/server";
import { requireToken } from "@/components/practitioner/session.server";
import { pp } from "@/components/practitioner/strings";

export const dynamic = "force-dynamic";
export const metadata = { title: pp("offerings.title") };

export default async function OfferingsPage() {
  const token = await requireToken("/gabinet-programisty/oferty");
  const [offerings, taxonomy] = await Promise.all([loadOfferings(token), loadTaxonomy(token)]);
  if (offerings.state === "none") return <CreateProfile />;
  if (offerings.state === "unavailable") return <Alert variant="danger">{pp("common.unavailable")}</Alert>;
  return <OfferingsManager initial={offerings.value} taxonomy={taxonomy.state === "ready" ? taxonomy.value : null} />;
}
