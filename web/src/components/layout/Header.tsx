import Link from "next/link";
import { t, type MessageKey } from "@/i18n/pl";
import styles from "./layout.module.css";

const NAV: ReadonlyArray<readonly [string, MessageKey]> = [
  ["/programisci", "nav.search"],
  ["/jak-to-dziala", "nav.howItWorks"],
  ["/zasady-rankingu", "nav.rankingRules"],
  ["/cennik", "nav.pricing"],
  ["/logowanie", "nav.signIn"],
  ["/rejestracja", "nav.register"],
];

export function Header() {
  return (
    <header className={styles.header}>
      <div className={styles.inner}>
        <Link href="/" className={styles.brand}>{t("site.name")}</Link>
        <nav aria-label={t("nav.label")}>
          <ul className={styles.nav}>
            {NAV.map(([href, key]) => (
              <li key={href}><Link href={href}>{t(key)}</Link></li>
            ))}
          </ul>
        </nav>
      </div>
    </header>
  );
}
