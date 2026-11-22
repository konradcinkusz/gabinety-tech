/**
 * Which web host the journeys target (ADR-0010: the Next.js app and the Blazor Web App run side by side until the cutover).
 * `HOST=next|blazor` (default next: nothing changes when it is unset). `BASE_URL` (alias `E2E_WEB_URL`) overrides the address;
 * the default is the host's own port (next 3000, blazor 3001). This is the ONE place that reads these variables; everything host-specific
 * (the health path, the sign-in helper, the circuit wait, the journeys the host supports) hangs off it.
 */
export type WebHost = "next" | "blazor";

const raw = (process.env.HOST ?? "next").trim().toLowerCase();
if (raw !== "next" && raw !== "blazor") throw new Error(`HOST must be "next" or "blazor", got "${raw}"`);

export const host: WebHost = raw;
export const isBlazor = host === "blazor";
export const webBaseUrl = (process.env.BASE_URL || process.env.E2E_WEB_URL || (isBlazor ? "http://localhost:3001" : "http://localhost:3000")).replace(/\/$/, "");
/** The web app's liveness path: the Next.js app has `/api/health`, the Blazor host the kernel's `/health`. */
export const webHealthPath = isBlazor ? "/health" : "/api/health";

/**
 * The spec files the Blazor host supports TODAY. Each port session adds its journey here once its pages exist.
 * With HOST=blazor ONLY these run; with HOST=next everything except `*.blazor.spec.ts` runs, exactly as before.
 */
export const BLAZOR_SPECS: readonly string[] = ["journeys/j7-foreign-origin.blazor.spec.ts"];
