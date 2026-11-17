import { notFound } from "next/navigation";
import { Alert, LinkButton, Pagination } from "@/components/ui";
import { t } from "@/i18n/pl";
import { listHref, withFilter } from "@/lib/seo/listUrl";
import { countFor, loadListContext, type RouteParams, type SearchParams } from "@/lib/seo/metadata";
import { listPractitioners } from "@/lib/seo/publicApi";
import { FilterLinks } from "./FilterLinks";
import { PractitionerCard } from "./PractitionerCard";
import styles from "./practitioners.module.css";
import { tp } from "./strings";

/** Server component shared by the three list routes. A failing Directory throws to the error boundary of the route. */
export async function ListPage({ params, search }: { params: RouteParams; search: SearchParams }) {
  const ctx = await loadListContext(params, search);
  const { state } = ctx;
  const page = await listPractitioners({
    problem: state.problem,
    tech: state.tech,
    q: state.q,
    duration: state.duration,
    maxPriceMinor: state.maxPriceMinor,
    cursor: state.cursor,
  });
  if (!page) notFound(); // the Directory refused the cursor: a stale or forged link

  const title = ctx.techName ? `${ctx.problemName}: ${ctx.techName}` : (ctx.problemName ?? tp("list.allTitle"));
  const intro = ctx.techName
    ? tp("list.categoryTechIntro", { name: title })
    : ctx.problemName
      ? tp("list.categoryIntro", { name: title })
      : tp("list.allIntro");
  const previous = state.cursor ? { href: listHref(withFilter(state, {})) } : null; // cursors are forward-only: back means the first page
  const next = page.nextCursor ? { href: listHref({ ...state, cursor: page.nextCursor }) } : null;

  return (
    <>
      <header className={styles.header}>
        <h1>{title}</h1>
        <p className={styles.intro}>{intro}</p>
        <p>{tp("list.count", { count: countFor(ctx) })}</p>
      </header>
      <div className={styles.layout}>
        <FilterLinks state={state} problems={ctx.taxonomy.problems} techs={ctx.taxonomy.techs} />
        <section aria-label={tp("list.resultsLabel")}>
          {page.items.length === 0 ? (
            <div className={styles.empty}>
              <Alert variant="warning">{tp("list.empty")}</Alert>
              <LinkButton href={listHref(withFilter({ problem: state.problem, tech: state.tech }, {}))}>{tp("list.clearFilters")}</LinkButton>
            </div>
          ) : (
            <ul className={styles.results}>
              {page.items.map((p) => (
                <li key={p.id}><PractitionerCard practitioner={p} /></li>
              ))}
            </ul>
          )}
          {(previous || next) && <Pagination previous={previous} next={next} />}
        </section>
      </div>
    </>
  );
}
