import type { ReactNode } from "react";
import "@/styles/tokens.css";
import { pl } from "@/i18n/pl";

export const metadata = { title: pl.site.name, description: pl.site.tagline };

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="pl">
      <body>{children}</body>
    </html>
  );
}
