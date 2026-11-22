import type { Page, WebSocket } from "@playwright/test";

/**
 * HOST=blazor, interactive pages (BLAZOR-WEB-APP section 13): the page is HTML first and interactive only after the circuit's websocket answers `StartCircuit`;
 * input before that is discarded without an error. Call `watchCircuit(page)` BEFORE navigating, `await` the returned promise before interacting.
 * It completes on the first frame the server sends after the client has sent its handshake and StartCircuit (two frames). A static page has no circuit:
 * do not await it there. Never sleeps; rejects on `timeoutMs`.
 */
export function watchCircuit(page: Page, timeoutMs = 15_000): Promise<void> {
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

/** Navigates and waits for the circuit; for a page that is interactive. */
export async function gotoInteractive(page: Page, path: string): Promise<void> {
  const ready = watchCircuit(page);
  await page.goto(path);
  await ready;
}
