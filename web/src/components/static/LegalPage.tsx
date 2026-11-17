import { Alert } from "@/components/ui";
import { formatDate } from "./ranking";
import { renderBlocks, type LegalDoc } from "./legal";
import styles from "./static.module.css";
import { ts } from "./strings";

export function LegalPage({ kind, doc }: { kind: "terms" | "privacy"; doc: LegalDoc }) {
  return (
    <div className={styles.prose}>
      <header className={styles.header}>
        <h1>{ts(kind === "terms" ? "legal.termsTitle" : "legal.privacyTitle")}</h1>
        <p className={styles.meta}>
          <span>{ts("legal.version", { version: doc.version })}</span>
          <span>{ts("legal.date", { date: formatDate(doc.date) })}</span>
        </p>
        {doc.draft && <Alert variant="warning" className={styles.notice}>{ts("legal.draftNotice")}</Alert>}
      </header>
      {renderBlocks(doc.body)}
    </div>
  );
}
