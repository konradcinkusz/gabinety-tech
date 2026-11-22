import { test, type APIRequestContext } from "@playwright/test";
import { urls } from "./env";
import { isBlazor } from "./host";

/**
 * Page-by-page parity between the two hosts (docs/operations/PARITY.md). On HOST=blazor a journey step that needs a page the Blazor host does not serve
 * yet is SKIPPED with a precise reason naming the port branch that owns the page; the skip disappears by itself when the page lands (the probe finds the route).
 * `PARITY_STRICT=1` (the cutover gate) turns every such skip into a failure. On HOST=next nothing happens.
 *
 * The probe is an anonymous GET without redirects: a route the host knows answers 200/30x (signed-out visitors are redirected to sign-in by the fallback
 * policy); a route it does not know answers 404. Dynamic segments are probed with a value that cannot exist, so only routes that answer 404 for a REAL
 * missing resource need a real value (the public profile: pass a seeded slug).
 */
export const OWNERS: ReadonlyArray<[prefix: string, owner: string]> = [
  ["/programisci", "port/public"],
  ["/gabinet/", "port/public"],
  ["/rejestracja", "port/auth"],
  ["/logowanie", "port/auth"],
  ["/konto", "port/auth"],
  ["/rezerwacja", "port/booking"],
  ["/checkout", "port/booking"],
  ["/moje", "port/booking"],
  ["/gabinet-programisty", "port/practitioner"],
  ["/admin", "port/admin"],
];
export const ownerOf = (route: string): string => OWNERS.find(([p]) => route.startsWith(p))?.[1] ?? "unowned";

/** Skips the running test (or fails it under PARITY_STRICT=1) when any of `routes` is not served by the Blazor host yet. */
export async function requirePorted(request: APIRequestContext, ...routes: string[]): Promise<void> {
  if (!isBlazor) return;
  const missing: string[] = [];
  for (const route of routes) {
    const res = await request.get(new URL(route, urls.web).toString(), { maxRedirects: 0, failOnStatusCode: false });
    if (res.status() === 404) missing.push(route);
  }
  if (missing.length === 0) return;
  const owners = [...new Set(missing.map(ownerOf))].join(", ");
  const reason = `not ported yet: ${missing.join(", ")} (owner ${owners}; docs/operations/PARITY.md)`;
  if (process.env.PARITY_STRICT === "1") throw new Error(reason);
  test.skip(true, reason);
}
