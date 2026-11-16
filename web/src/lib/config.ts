// Runtime configuration read per request, never baked at build time (FRONTEND-BFF section 2).
export type RuntimeConfig = { siteBaseUrl: string; backends: Record<string, string[]> };

const SERVICES = ["Directory", "Booking", "Reputation", "Assistant", "Auth"] as const;

export function readConfig(env: Record<string, string | undefined> = process.env): RuntimeConfig {
  const backends: Record<string, string[]> = {};
  for (const s of SERVICES) {
    const raw = env[`Backend__${s}`] ?? "";
    backends[s.toLowerCase()] = raw.split(",").map((x) => x.trim()).filter(Boolean);
  }
  return { siteBaseUrl: env["Site__BaseUrl"] ?? "http://localhost:3000", backends };
}
