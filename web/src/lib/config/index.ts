// Runtime configuration read per request, never baked at build time (FRONTEND-BFF section 2).
// Server-side only: backend addresses and identity settings never reach the browser.
export type RuntimeConfig = {
  siteBaseUrl: string;
  backends: Record<string, string[]>;
  identity: { issuer?: string; audience?: string; publicUrl?: string };
};

export const SERVICES = ["Directory", "Booking", "Reputation", "Assistant", "Auth"] as const;

const opt = (v: string | undefined) => (v && v.trim() ? v.trim() : undefined);

export function readConfig(env: Record<string, string | undefined> = process.env): RuntimeConfig {
  const backends: Record<string, string[]> = {};
  for (const s of SERVICES) {
    const raw = env[`Backend__${s}`] ?? "";
    backends[s.toLowerCase()] = raw.split(",").map((x) => x.trim().replace(/\/+$/, "")).filter(Boolean);
  }
  return {
    siteBaseUrl: (opt(env["Site__BaseUrl"]) ?? "http://localhost:3000").replace(/\/+$/, ""),
    backends,
    identity: {
      issuer: opt(env["Identity__Issuer"]),
      audience: opt(env["Identity__Audience"]),
      publicUrl: opt(env["Identity__PublicUrl"]),
    },
  };
}

/** The only shape the browser may see: no address, key, token or secret. */
export function publicConfig(config: RuntimeConfig = readConfig()) {
  return { siteBaseUrl: config.siteBaseUrl, authEnabled: config.backends.auth.length > 0 };
}
