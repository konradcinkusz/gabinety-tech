import type { Page, Response } from "@playwright/test";
import { isBlazor } from "./env";

/**
 * `awaitCircuit` (BLAZOR-WEB-APP section 13): a Blazor interactive page is HTML first and interactive only after the websocket answers `StartCircuit`;
 * input before that is discarded WITHOUT an error. So a journey must not click, type or submit on an interactive page until the circuit is live, and must
 * not sleep for it either. The watch subscribes to the page's websocket BEFORE navigating and completes on the first frame the server sends after the client
 * sent `StartCircuit`. On HOST=next every function here is a no-op, so one journey runs against both hosts.
 *
 * A static page has no circuit (ADR-0010 decision 2): `gotoInteractive` looks at the prerendered HTML for the server component marker and waits only when
 * there is one, so using it on a static page costs nothing and never hangs.
 */
export interface CircuitWatch {
  /** Resolves when the circuit is live; rejects after `timeoutMs` (a timeout of the wait, not a sleep). */
  ready(timeoutMs?: number): Promise<void>;
}

const text = (payload: string | Buffer): string => (typeof payload === "string" ? payload : payload.toString("latin1"));

export function watchCircuit(page: Page): CircuitWatch {
  if (!isBlazor) return { ready: async () => {} };
  let live!: () => void;
  const done = new Promise<void>((resolve) => (live = resolve));
  page.on("websocket", (ws) => {
    let started = false; // per socket: a page that navigates opens a new one, and an old socket's frames must not count
    ws.on("framesent", (f) => { if (text(f.payload).includes("StartCircuit")) started = true; });
    ws.on("framereceived", () => { if (started) live(); });
  });
  return {
    ready: (timeoutMs = 20_000) =>
      new Promise<void>((resolve, reject) => {
        const t = setTimeout(() => reject(new Error(`the Blazor circuit did not start within ${timeoutMs} ms (no StartCircuit answer on the websocket)`)), timeoutMs);
        void done.then(() => { clearTimeout(t); resolve(); });
      }),
  };
}

/** True when the prerendered HTML of an answer carries a server-interactive component marker (the page will start a circuit). */
async function hasCircuit(res: Response | null): Promise<boolean> {
  if (!res) return false;
  const html = await res.text().catch(() => "");
  return /<!--Blazor:\{[^}]*"type":"server"/.test(html);
}

/** `page.goto` that, on the Blazor host and an interactive page, returns only once the circuit is live. Same return value as `goto`. */
export async function gotoInteractive(page: Page, url: string): Promise<Response | null> {
  const circuit = watchCircuit(page);
  const res = await page.goto(url);
  if (isBlazor && (await hasCircuit(res))) await circuit.ready();
  return res;
}

/** For a page that navigated by itself (a link click, a form post redirect): wait for its circuit after the fact, from a watch taken before the action. */
export async function awaitCircuit(circuit: CircuitWatch): Promise<void> {
  await circuit.ready();
}
