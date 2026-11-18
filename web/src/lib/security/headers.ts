// Security response headers (SECURITY-REVIEW F-02, T-10.09). Two parts:
//  - staticHeaders(): the same on every response, applied by next.config.ts `headers()`;
//  - buildCsp(nonce): the Content-Security-Policy, built per request in src/proxy.ts because the
//    script nonce must be fresh each time (Next docs: guides/content-security-policy).

export type Header = { key: string; value: string };

export function buildCsp(nonce: string, dev = false): string {
  const directives = [
    "default-src 'self'",
    // Nonce + 'strict-dynamic': Next's own inline bootstrap scripts carry the nonce (Next reads it from this
    // header) and the chunks they load inherit trust. No 'unsafe-inline' and, in production, no 'unsafe-eval';
    // 'unsafe-eval' is added in `next dev` only, where React rebuilds server stacks with eval.
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${dev ? " 'unsafe-eval'" : ""}`,
    // No inline style attributes are left in the app (they were moved to a CSS class), so no 'unsafe-inline'.
    `style-src 'self' 'nonce-${nonce}'`,
    "img-src 'self' data: blob:",
    "font-src 'self'",
    // Browser calls go to same-origin /api/* only (the BFF proxy); the app has no external hosts.
    "connect-src 'self'",
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
    "frame-ancestors 'none'",
  ];
  return directives.join("; ");
}

/** For responses the proxy does not see (the matcher skips /api and static assets): nothing renders there. */
export const API_CSP = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

export function staticHeaders(production: boolean): Header[] {
  const h: Header[] = [
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    {
      key: "Permissions-Policy",
      value: [
        "accelerometer", "autoplay", "browsing-topics", "camera", "display-capture", "fullscreen", "geolocation",
        "gyroscope", "hid", "interest-cohort", "magnetometer", "microphone", "midi", "payment", "publickey-credentials-get",
        "screen-wake-lock", "serial", "usb", "xr-spatial-tracking",
      ].map((f) => `${f}=()`).join(", "),
    },
    // Legacy agents that do not understand CSP frame-ancestors.
    { key: "X-Frame-Options", value: "DENY" },
  ];
  // HSTS only where the site is served over TLS; in dev/test it would pin localhost to https.
  if (production) h.push({ key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" });
  return h;
}
