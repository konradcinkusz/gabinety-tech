export const BRIEF_MIN = 20;
export const BRIEF_MAX = 2000;

/** CONTRACTS 4.2: the brief is 20 to 2000 characters after trimming. */
export function briefProblem(text: string): "short" | "long" | null {
  const n = text.trim().length;
  return n < BRIEF_MIN ? "short" : n > BRIEF_MAX ? "long" : null;
}

/** SPEC 6.5: the mock checkout exists only in the fake profile or when the provider is Mock. Unknown means no. */
export function isMockPayments(env: Record<string, string | undefined> = process.env): boolean {
  const provider = env["Payments__Provider"]?.trim();
  if (provider) return provider.toLowerCase() === "mock";
  return env["GABINETY_FAKE"] === "1";
}

/** Where to send the browser after a booking is created: a page of this site, or an https vendor page. Anything else is refused. */
export function safeCheckoutTarget(url: string, origin: string): string | null {
  if (url.startsWith("/") && !url.startsWith("//")) return url;
  let u: URL;
  try {
    u = new URL(url);
  } catch {
    return null;
  }
  if (u.origin === origin) return u.pathname + u.search;
  return u.protocol === "https:" ? u.href : null;
}

/** A link from the server is rendered only when it is http or https. */
export function safeHttpUrl(url: string | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url);
    return u.protocol === "https:" || u.protocol === "http:" ? u.href : null;
  } catch {
    return null;
  }
}

/** `m:ss` for a number of milliseconds, never negative. */
export function clock(ms: number): string {
  const s = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`;
}
