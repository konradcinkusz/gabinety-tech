import { Alert } from "@/components/ui";
import { AvailabilityEditor } from "@/components/practitioner/AvailabilityEditor";
import { CreateProfile } from "@/components/practitioner/CreateProfile";
import { loadAvailability, loadProfile } from "@/components/practitioner/server";
import { requireToken } from "@/components/practitioner/session.server";
import { pp } from "@/components/practitioner/strings";

export const dynamic = "force-dynamic";
export const metadata = { title: pp("availability.title") };

export default async function AvailabilityPage() {
  const token = await requireToken("/gabinet-programisty/dostepnosc");
  // Booking answers 403 to a user without a profile; ask Directory first so that case shows the create step, not an error.
  const profile = await loadProfile(token);
  if (profile.state === "none") return <CreateProfile />;
  const availability = await loadAvailability(token);
  if (profile.state === "unavailable" || availability.state !== "ready") return <Alert variant="danger">{pp("common.unavailable")}</Alert>;
  return <AvailabilityEditor initial={availability.value} />;
}
