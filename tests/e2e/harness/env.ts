/** Where the fake profile listens (AppHost `Program.cs`, `scripts/check-fake-profile.sh`). Overridable for a non-default setup. */
const e = (name: string, fallback: string) => process.env[name] || fallback;

export const urls = {
  web: e("E2E_WEB_URL", process.env.BASE_URL || "http://localhost:3000").replace(/\/$/, ""), // the Blazor host (src/Gabinety.Web); BASE_URL is an alias
  devidentity: e("E2E_IDENTITY_URL", "http://localhost:5100"),
  directory: e("E2E_DIRECTORY_URL", "http://localhost:5101"),
  booking: e("E2E_BOOKING_URL", "http://localhost:5102"),
  reputation: e("E2E_REPUTATION_URL", "http://localhost:5103"),
  assistant: e("E2E_ASSISTANT_URL", "http://localhost:5104"),
} as const;

/** `/health` of every service, in start order (the web app, the Blazor host, answers the kernel's `/health` too). */
export const healthUrls: Record<string, string> = {
  devidentity: `${urls.devidentity}/health`,
  directory: `${urls.directory}/health`,
  booking: `${urls.booking}/health`,
  reputation: `${urls.reputation}/health`,
  assistant: `${urls.assistant}/health`,
  web: `${urls.web}/health`,
};

/**
 * The fake profile's development key and password. Both are fixed, documented, non-secret values of the fake profile
 * (AppHost `internal-key`, tools/Gabinety.DevIdentity/README.md); they protect nothing and are overridable.
 */
export const internalKey = e("E2E_INTERNAL_KEY", "demo-development-internal-key-not-a-secret");
export const demoPassword = e("E2E_DEMO_PASSWORD", "Demo-Dev-Passw0rd!");

/** The seeded demo users (SPEC 6.7). E-mail is `<name>@demo.example`. */
export const demoUsers = {
  client: "demo-client",
  admin: "demo-admin",
  practitioner: (n: 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8) => `demo-practitioner-${n}`,
} as const;
export const emailOf = (userName: string) => `${userName}@demo.example`;

/** The approved demo practitioners the public list shows (tools/Gabinety.DemoSeeder `DemoCatalog`). */
export const APPROVED_DEMO_PRACTITIONERS = [
  { slug: "demo-anna-demowska", name: "Anna Demowska" },
  { slug: "demo-bartosz-testowy", name: "Bartosz Testowy" },
  { slug: "demo-celina-przykladowska", name: "Celina Przykładowska" },
  { slug: "demo-dariusz-fikcyjny", name: "Dariusz Fikcyjny" },
  { slug: "demo-ewa-wzorcowa", name: "Ewa Wzorcowa" },
  { slug: "demo-filip-syntetyczny", name: "Filip Syntetyczny" },
] as const;
