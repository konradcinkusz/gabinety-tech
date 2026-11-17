import Link from "next/link";
import type { ReactNode } from "react";
import { pp } from "@/components/practitioner/strings";

// SPEC 6.5: noindex on everything under /gabinet-programisty.
export const metadata = { title: pp("nav.label"), robots: { index: false, follow: false } };

export default function PractitionerLayout({ children }: { children: ReactNode }) {
  return (
    <>
      <nav aria-label={pp("nav.label")}>
        <Link href="/gabinet-programisty">{pp("nav.overview")}</Link> · <Link href="/gabinet-programisty/profil">{pp("nav.profile")}</Link> ·{" "}
        <Link href="/gabinet-programisty/oferty">{pp("nav.offerings")}</Link> · <Link href="/gabinet-programisty/dostepnosc">{pp("nav.availability")}</Link>
      </nav>
      {children}
    </>
  );
}
