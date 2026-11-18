import type { ReactNode } from "react";
import "@/styles/tokens.css";
import "@/styles/global.css";
import { Footer } from "@/components/layout/Footer";
import { Header } from "@/components/layout/Header";
import styles from "@/components/layout/layout.module.css";
import { t } from "@/i18n/pl";

// The CSP carries a per-request script nonce (src/proxy.ts), so no page may be prerendered: a static page would
// ship inline scripts without the nonce and the browser would block them.
export const dynamic = "force-dynamic";

export const metadata = { title: t("site.name"), description: t("site.tagline") };

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="pl">
      <body>
        <a href="#main" className={styles.skip}>{t("a11y.skipToContent")}</a>
        <Header />
        <main id="main" tabIndex={-1} className={styles.main}>{children}</main>
        <Footer year={new Date().getUTCFullYear()} />
      </body>
    </html>
  );
}
