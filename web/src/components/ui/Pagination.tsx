import Link from "next/link";
import type { ReactNode } from "react";
import { t } from "@/i18n/pl";
import styles from "./Pagination.module.css";

export type PageTarget = { href: string } | { onSelect: () => void };

export interface PaginationProps {
  previous: PageTarget | null;
  next: PageTarget | null;
}

function Item({ target, children }: { target: PageTarget | null; children: ReactNode }) {
  if (!target) {
    return <span aria-disabled="true" className={`${styles.item} ${styles.disabled}`}>{children}</span>;
  }
  if ("href" in target) {
    return <Link href={target.href} className={styles.item}>{children}</Link>;
  }
  return <button type="button" onClick={target.onSelect} className={styles.item}>{children}</button>;
}

export function Pagination({ previous, next }: PaginationProps) {
  return (
    <nav aria-label={t("a11y.pagination")} className={styles.nav}>
      <Item target={previous}>{t("common.previous")}</Item>
      <Item target={next}>{t("common.next")}</Item>
    </nav>
  );
}
