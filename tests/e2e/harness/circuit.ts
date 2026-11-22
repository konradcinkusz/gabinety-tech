import type { Page, Response, WebSocket } from "@playwright/test";

/**
 * Interactive pages (BLAZOR-WEB-APP section 13): the page is HTML first and interactive only after the circuit's websocket answers `StartCircuit`;
 * input before that is discarded without an error. Call `watchCircuit(page)` BEFORE navigating (or clicking a link to an interactive page) and `await` the returned
 * promise before interacting. It completes on the first frame the server sends after the client has sent its handshake and StartCircuit (two frames). It never
 * sleeps; it rejects after `timeoutMs`. A static page has no circuit: do not await it there
 * (`gotoInteractive` looks at the prerendered HTML and waits only when there is a server component marker, so it is safe on either kind of page).
 */
export function watchCircuit(page: Page, timeoutMs = 15_000): Promise<void> {
  const watch = new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error("the circuit did not start (no frame after StartCircuit)")), timeoutMs);
    page.on("websocket", (ws: WebSocket) => {
      if (!ws.url().includes("/_blazor")) return;
      let sent = 0;
      ws.on("framesent", () => { sent++; });
      ws.on("framereceived", () => { if (sent >= 2) { clearTimeout(timer); resolve(); } });
    });
  });
  watch.catch(() => {}); // a watch for a page that turns out to be static is never awaited: its timeout is not an unhandled rejection; an awaiter still sees the rejection
  return watch;
}

/** `awaitCircuit(watch)`: the name the guide uses for awaiting a watch taken before the action. */
export const awaitCircuit = (watch: Promise<void>): Promise<void> => watch;

/** True when the prerendered HTML of an answer carries a server-interactive component marker (the page will start a circuit). */
export async function hasCircuit(res: Response | null): Promise<boolean> {
  if (!res) return false;
  const html = await res.text().catch(() => "");
  return /<!--Blazor:\{[^}]*"type":"server"/.test(html);
}

/** `page.goto` that, on an interactive page, returns only once the circuit is live (a static page returns at once). */
export async function gotoInteractive(page: Page, path: string): Promise<Response | null> {
  const ready = watchCircuit(page);
  ready.catch(() => {}); // a static page never starts one: the timeout must not surface as an unhandled rejection
  const res = await page.goto(path);
  if (await hasCircuit(res)) await ready;
  return res;
}

/**
 * After an action that navigates (a link click, a form post): waits for the circuit only when the page that answered is interactive. Take `watch = watchCircuit(page)` BEFORE the action and
 * the answer with `page.waitForResponse` (document) before it too. A static page (the booking form is a plain form post) has no circuit and returns at once.
 */
export async function awaitCircuitIfInteractive(res: Response, watch: Promise<void>): Promise<void> {
  watch.catch(() => {}); // not awaited for a static page: its timeout must not become an unhandled rejection
  if (await hasCircuit(res)) await watch;
}
