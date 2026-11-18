import { isIP } from "node:net";

const valid = (raw: string | null | undefined): string | undefined => {
  const v = raw?.trim();
  return v && v.length <= 45 && isIP(v) !== 0 ? v : undefined;
};

/**
 * The visitor's address as the platform in front of this server reports it (CONTRACTS 10.2: the limit is per client address). The platform's own header wins
 * (`Fly-Client-IP`, set by the edge and not by the visitor); otherwise the LAST entry of `X-Forwarded-For`, which the nearest proxy appended, never the first,
 * which the visitor can write. Not a single valid IP address: undefined, and the services then see the address of this machine.
 */
export function clientAddressOf(headers: Headers): string | undefined {
  const platform = valid(headers.get("fly-client-ip"));
  if (platform) return platform;
  const xff = headers.get("x-forwarded-for");
  return xff ? valid(xff.split(",").pop()) : undefined;
}
