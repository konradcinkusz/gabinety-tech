/** F-09: a portfolio link is rendered (and listed in the JSON-LD) only when it is https. The server already refuses anything else; this is the second check. */
export function safeHttpsUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url);
    return u.protocol === "https:" ? u.href : null;
  } catch {
    return null;
  }
}
