/**
 * One HTTP helper for every call the harness makes outside the browser. The Directory limits each client address to 60 requests a minute
 * (CONTRACTS 10.2) and the seeder, Booking, the web app and the tests all arrive from localhost, so a 429 is normal, not a failure:
 * wait for `Retry-After` (else a growing delay) and try again. The wait is for the *rate limit*, never for business time (use the clock for that).
 */
export interface RetryOptions {
  /** Total time to keep retrying a 429/503/network error. Default 120 s (a seeder run that hit the limit takes ~80 s, see PR #93). */
  budgetMs?: number;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

export async function fetchRetrying(url: string, init: RequestInit = {}, { budgetMs = 120_000 }: RetryOptions = {}): Promise<Response> {
  const deadline = Date.now() + budgetMs;
  let delay = 500;
  for (;;) {
    let res: Response | undefined;
    try {
      res = await fetch(url, { ...init, signal: init.signal ?? AbortSignal.timeout(30_000) });
    } catch (err) {
      if (Date.now() + delay > deadline) throw err;
    }
    if (res && res.status !== 429 && res.status !== 503) return res;
    if (Date.now() + delay > deadline) {
      if (res) return res; // out of budget: hand the 429/503 to the caller, who fails with a real status
      throw new Error(`${url}: no answer within the retry budget`);
    }
    const retryAfter = Number(res?.headers.get("retry-after"));
    await sleep(Number.isFinite(retryAfter) && retryAfter > 0 ? Math.min(retryAfter * 1000, 65_000) : delay);
    delay = Math.min(delay * 2, 10_000);
  }
}

export async function json<T = unknown>(res: Response, what: string): Promise<T> {
  if (!res.ok) throw new Error(`${what}: HTTP ${res.status}`); // status only: no body, no user text in test logs
  return (await res.json()) as T;
}
