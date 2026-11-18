import type { NextConfig } from "next";
import { API_CSP, staticHeaders } from "./src/lib/security/headers";

const config: NextConfig = {
  output: "standalone",
  poweredByHeader: false,
  async headers() {
    return [
      { source: "/:path*", headers: staticHeaders(process.env.NODE_ENV === "production") },
      // Page CSP (with a nonce) is set in src/proxy.ts; the proxy skips /api, so give it a locked-down one here.
      { source: "/api/:path*", headers: [{ key: "Content-Security-Policy", value: API_CSP }] },
    ];
  },
};
export default config;
