import Link from "next/link";
import styles from "./static.module.css";
import { ts } from "./strings";

export function HowItWorksPage() {
  return (
    <div className={styles.prose}>
      <header className={styles.header}>
        <h1>{ts("how.title")}</h1>
        <p className={styles.lead}>{ts("how.intro")}</p>
      </header>
      <section className={styles.section} aria-labelledby="how-client">
        <h2 id="how-client">{ts("how.clientTitle")}</h2>
        <ol>
          <li>{ts("how.c1")}</li>
          <li>{ts("how.c2")}</li>
          <li>{ts("how.c3")}</li>
          <li>{ts("how.c4")}</li>
          <li>{ts("how.c5")}</li>
        </ol>
      </section>
      <section className={styles.section} aria-labelledby="how-pay">
        <h2 id="how-pay">{ts("how.paymentTitle")}</h2>
        <ul>
          <li>{ts("how.pay1")}</li>
          <li>{ts("how.pay2")}</li>
          <li>{ts("how.pay3")}</li>
        </ul>
      </section>
      <section className={styles.section} aria-labelledby="how-practitioner">
        <h2 id="how-practitioner">{ts("how.practitionerTitle")}</h2>
        <ol>
          <li>{ts("how.p1")}</li>
          <li>{ts("how.p2")}</li>
          <li>{ts("how.p3")}</li>
        </ol>
      </section>
      <p>
        {ts("how.seeAlso")}: <Link href="/zasady-rankingu">{ts("home.rulesLink")}</Link>
        {" · "}
        <Link href="/cennik">{ts("home.pricingLink")}</Link>
      </p>
    </div>
  );
}
