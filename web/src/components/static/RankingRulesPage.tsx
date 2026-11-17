import { Alert } from "@/components/ui";
import { EXAMPLE_INPUT, formatDate, formatFixed, formatNumber, formatPercent, workedExample, type PartKey } from "./ranking";
import { getRankingRules } from "./rankingApi";
import styles from "./static.module.css";
import { ts, type TextKey } from "./strings";

const MEANING: Record<PartKey, TextKey> = {
  rating: "ranking.ratingMeaning",
  reliability: "ranking.reliabilityMeaning",
  availability: "ranking.availabilityMeaning",
  completeness: "ranking.completenessMeaning",
};

/** Server component. A failing Directory throws to the route's error boundary: stale numbers are never shown as the rules. */
export async function RankingRulesPage() {
  const rules = await getRankingRules();
  const example = workedExample(rules);
  const i = EXAMPLE_INPUT;
  return (
    <div className={styles.prose}>
      <header className={styles.header}>
        <h1>{ts("ranking.title")}</h1>
        <p className={styles.lead}>{ts("ranking.intro")}</p>
        <p className={styles.meta}>
          <span>{ts("ranking.version", { version: rules.version })}</span>
          <span>{ts("ranking.effectiveFrom", { date: formatDate(rules.effectiveFrom) })}</span>
        </p>
        <Alert variant="info" className={styles.notice}>
          <span className={styles.emphasis}>{ts("ranking.nothingPaid")}</span>
        </Alert>
      </header>

      <section className={styles.section} aria-labelledby="rank-weights">
        <h2 id="rank-weights">{ts("ranking.weightsTitle")}</h2>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption>{ts("ranking.weightsCaption")}</caption>
            <thead>
              <tr>
                <th scope="col">{ts("ranking.colPart")}</th>
                <th scope="col" className={styles.num}>{ts("ranking.colWeight")}</th>
                <th scope="col">{ts("ranking.colMeaning")}</th>
              </tr>
            </thead>
            <tbody>
              {(Object.keys(MEANING) as PartKey[]).map((k) => (
                <tr key={k}>
                  <th scope="row">{ts(`ranking.${k}`)}</th>
                  <td className={styles.num}>{formatPercent(rules.weights[k])}</td>
                  <td>{ts(MEANING[k])}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <p>{ts("ranking.scoreLine")} {ts("ranking.tieBreak")}</p>
      </section>

      <section className={styles.section} aria-labelledby="rank-constants">
        <h2 id="rank-constants">{ts("ranking.constantsTitle")}</h2>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption>{ts("ranking.constantsCaption")}</caption>
            <thead>
              <tr>
                <th scope="col">{ts("ranking.colConstant")}</th>
                <th scope="col" className={styles.num}>{ts("ranking.colValue")}</th>
              </tr>
            </thead>
            <tbody>
              <tr><th scope="row">{ts("ranking.constM")}</th><td className={styles.num}>{formatNumber(rules.constants.m)}</td></tr>
              <tr><th scope="row">{ts("ranking.constMu")}</th><td className={styles.num}>{formatNumber(rules.constants.mu)}</td></tr>
              <tr><th scope="row">{ts("ranking.constMin")}</th><td className={styles.num}>{formatNumber(rules.constants.minReviewsToShowRating)}</td></tr>
              <tr><th scope="row">{ts("ranking.constSlots")}</th><td className={styles.num}>{formatNumber(rules.constants.slotsForFullAvailability)}</td></tr>
            </tbody>
          </table>
        </div>
        <p>{ts("ranking.smallSample")}</p>
      </section>

      <section className={styles.section} aria-labelledby="rank-example">
        <h2 id="rank-example">{ts("ranking.exampleTitle")}</h2>
        <p>
          {ts("ranking.exampleIntro", {
            n: i.n,
            r: formatNumber(i.r),
            c: i.c,
            x: i.x,
            p: formatNumber(i.p),
            k: i.k,
          })}
        </p>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption>{ts("ranking.exampleCaption")}</caption>
            <thead>
              <tr>
                <th scope="col">{ts("ranking.colPart")}</th>
                <th scope="col" className={styles.num}>{ts("ranking.colInput")}</th>
                <th scope="col" className={styles.num}>{ts("ranking.colWeight")}</th>
                <th scope="col" className={styles.num}>{ts("ranking.colContribution")}</th>
              </tr>
            </thead>
            <tbody>
              {example.parts.map((p) => (
                <tr key={p.key}>
                  <th scope="row">{ts(`ranking.${p.key}`)}</th>
                  <td className={styles.num}>{formatFixed(p.value, 3)}</td>
                  <td className={styles.num}>{formatPercent(p.weight)}</td>
                  <td className={styles.num}>{formatFixed(p.points)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row" colSpan={3}>{ts("ranking.exampleTotal")}</th>
                <td className={styles.num}><span className={styles.emphasis}>{formatFixed(example.score)}</span></td>
              </tr>
            </tfoot>
          </table>
        </div>
      </section>
    </div>
  );
}
