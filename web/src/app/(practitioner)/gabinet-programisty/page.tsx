import { Alert } from "@/components/ui";
import { CreateProfile } from "@/components/practitioner/CreateProfile";
import { Onboarding } from "@/components/practitioner/Onboarding";
import { loadOfferings, loadProfile } from "@/components/practitioner/server";
import { requireToken } from "@/components/practitioner/session.server";
import { pp } from "@/components/practitioner/strings";

export const dynamic = "force-dynamic";

export default async function PractitionerHome() {
  const token = await requireToken("/gabinet-programisty");
  const [profile, offerings] = await Promise.all([loadProfile(token), loadOfferings(token)]);
  if (profile.state === "none") return <CreateProfile />;
  if (profile.state === "unavailable" || offerings.state === "unavailable") return <Alert variant="danger">{pp("common.unavailable")}</Alert>;
  return <Onboarding initialProfile={profile.value} initialOfferings={offerings.state === "ready" ? offerings.value : []} />;
}
