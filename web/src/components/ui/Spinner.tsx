import { t } from "@/i18n/pl";
import styles from "./Spinner.module.css";

export function Spinner() {
  return (
    <span role="status" className={styles.wrap}>
      <span className={styles.spinner} aria-hidden="true" />
      <span className="sr-only">{t("a11y.spinnerLabel")}</span>
    </span>
  );
}
