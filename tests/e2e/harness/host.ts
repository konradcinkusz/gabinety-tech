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
 * Which specs run where is decided by `playwright.config.ts`: both hosts run every journey; `*.blazor.spec.ts` is Blazor-only, `*.next.spec.ts` (and the original j7) Next-only.
 * A journey step that needs a page the Blazor host has not got yet skips itself with the owning branch named (`harness/parity.ts`); `PARITY_STRICT=1` makes that a failure.
 */
