import { Alert } from "@/components/ui";
import { CreateProfile } from "@/components/practitioner/CreateProfile";
import { ProfileEditor } from "@/components/practitioner/ProfileEditor";
import { loadProfile } from "@/components/practitioner/server";
import { requireToken } from "@/components/practitioner/session.server";
import { pp } from "@/components/practitioner/strings";

export const dynamic = "force-dynamic";
export const metadata = { title: pp("profile.title") };

export default async function ProfilePage() {
  const profile = await loadProfile(await requireToken("/gabinet-programisty/profil"));
  if (profile.state === "none") return <CreateProfile />;
  if (profile.state === "unavailable") return <Alert variant="danger">{pp("common.unavailable")}</Alert>;
  return <ProfileEditor initial={profile.value} />;
}
