import Link from "next/link";
import { t } from "@/i18n/pl";
import styles from "./layout.module.css";

export function Footer({ year }: { year: number }) {
  return (
    <footer className={styles.footer}>
      <div className={styles.inner}>
        <p>{t("footer.copyright", { year })}</p>
        <ul className={styles.nav}>
          <li><Link href="/regulamin">{t("footer.terms")}</Link></li>
          <li><Link href="/prywatnosc">{t("footer.privacy")}</Link></li>
        </ul>
      </div>
    </footer>
  );
}
