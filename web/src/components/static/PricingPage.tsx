import { formatPln } from "@/components/ui";
import { feeFor, formatBps } from "./fee";
import styles from "./static.module.css";
import { ts } from "./strings";

/** An example price of 250,00 zl, the first published vector of CONTRACTS 9.1. */
const EXAMPLE_PRICE_MINOR = 25_000;

export function PricingPage({ feeBps }: { feeBps: number }) {
  const fee = feeFor(EXAMPLE_PRICE_MINOR, feeBps);
  return (
    <div className={styles.prose}>
      <header className={styles.header}>
        <h1>{ts("pricing.title")}</h1>
        <p className={styles.lead}>{ts("pricing.intro")}</p>
      </header>
      <section className={styles.section} aria-labelledby="pricing-rule">
        <h2 id="pricing-rule">{ts("pricing.onlyFirst")}</h2>
        <p>{ts("pricing.rule", { rate: formatBps(feeBps) })}</p>
        <ul>
          <li>{ts("pricing.later")}</li>
          <li>{ts("pricing.absorbed")}</li>
          <li>{ts("pricing.rounding")}</li>
        </ul>
        <p>{ts("pricing.refundNote")}</p>
      </section>
      <section className={styles.section} aria-labelledby="pricing-example">
        <h2 id="pricing-example">{ts("pricing.exampleTitle")}</h2>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption>{ts("pricing.exampleCaption")}</caption>
            <thead>
              <tr>
                <th scope="col">{ts("pricing.colItem")}</th>
                <th scope="col" className={styles.num}>{ts("pricing.colAmount")}</th>
              </tr>
            </thead>
            <tbody>
              <tr><th scope="row">{ts("pricing.price")}</th><td className={styles.num}>{formatPln(EXAMPLE_PRICE_MINOR)}</td></tr>
              <tr><th scope="row">{ts("pricing.fee")}</th><td className={styles.num}>{formatPln(fee)}</td></tr>
              <tr><th scope="row">{ts("pricing.payout")}</th><td className={styles.num}>{formatPln(EXAMPLE_PRICE_MINOR - fee)}</td></tr>
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
