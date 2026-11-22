import type { Page, Response, WebSocket } from "@playwright/test";
import { isBlazor } from "./host";

/**
 * HOST=blazor, interactive pages (BLAZOR-WEB-APP section 13): the page is HTML first and interactive only after the circuit's websocket answers `StartCircuit`;
 * input before that is discarded without an error. Call `watchCircuit(page)` BEFORE navigating (or clicking a link to an interactive page) and `await` the returned
 * promise before interacting. It completes on the first frame the server sends after the client has sent its handshake and StartCircuit (two frames). It never
 * sleeps; it rejects after `timeoutMs`. On HOST=next it resolves at once, so one journey runs against both hosts. A static page has no circuit: do not await it there
 * (`gotoInteractive` looks at the prerendered HTML and waits only when there is a server component marker, so it is safe on either kind of page).
 */
export function watchCircuit(page: Page, timeoutMs = 15_000): Promise<void> {
  if (!isBlazor) return Promise.resolve();
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("the circuit did not start (no frame after StartCircuit)")), timeoutMs);
    page.on("websocket", (ws: WebSocket) => {
      if (!ws.url().includes("/_blazor")) return;
      let sent = 0;
      ws.on("framesent", () => { sent++; });
      ws.on("framereceived", () => { if (sent >= 2) { clearTimeout(timer); resolve(); } });
    });
  });
}

/** `awaitCircuit(watch)`: the name the guide uses for awaiting a watch taken before the action. */
export const awaitCircuit = (watch: Promise<void>): Promise<void> => watch;

/** True when the prerendered HTML of an answer carries a server-interactive component marker (the page will start a circuit). */
async function hasCircuit(res: Response | null): Promise<boolean> {
  if (!res) return false;
  const html = await res.text().catch(() => "");
  return /<!--Blazor:\{[^}]*"type":"server"/.test(html);
}

/** `page.goto` that, on the Blazor host and an interactive page, returns only once the circuit is live (a static page returns at once). */
export async function gotoInteractive(page: Page, path: string): Promise<Response | null> {
  const ready = watchCircuit(page);
  ready.catch(() => {}); // a static page never starts one: the timeout must not surface as an unhandled rejection
  const res = await page.goto(path);
  if (isBlazor && (await hasCircuit(res))) await ready;
  return res;
}
