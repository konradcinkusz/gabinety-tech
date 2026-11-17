import { waitForProfile } from "./wait-for-profile.cjs";

/** Starts nothing: waits on `/health` of every service and for the seeded practitioners (the seeder runs after the services are healthy). */
export default async function globalSetup() {
  await waitForProfile();
}
