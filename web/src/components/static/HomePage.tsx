import Link from "next/link";
import { Alert, Button, Field, TextInput } from "@/components/ui";
import { getTaxonomy, type Category } from "@/lib/seo/publicApi";
import styles from "./static.module.css";
import { ts } from "./strings";

/** Categories are a region of the page: a failing Directory shows an error state there, the rest of the page stays (SPEC 6.5). */
async function loadCategories(): Promise<Category[] | null> {
  try {
    return (await getTaxonomy()).problems;
  } catch {
    return null;
  }
}

const STEPS = [
  ["home.step1Title", "home.step1Body"],
  ["home.step2Title", "home.step2Body"],
  ["home.step3Title", "home.step3Body"],
] as const;

export async function HomePage() {
  const problems = await loadCategories();
  return (
    <>
      <header className={styles.header}>
        <h1>{ts("home.title")}</h1>
        <p className={styles.lead}>{ts("home.lead")}</p>
      </header>

      {/* A plain GET form: works without JavaScript. */}
      <form method="get" action="/programisci" role="search" aria-label={ts("home.searchRegion")} className={styles.search}>
        <Field label={ts("home.searchLabel")}>
          {(control) => <TextInput {...control} name="q" maxLength={60} placeholder={ts("home.searchPlaceholder")} autoComplete="off" />}
        </Field>
        <div>
          <Button type="submit">{ts("home.searchButton")}</Button>
        </div>
      </form>

      <section className={styles.section} aria-labelledby="home-categories">
        <h2 id="home-categories">{ts("home.categoriesTitle")}</h2>
        {problems ? (
          <ul className={styles.links} aria-label={ts("home.categoriesLabel")}>
            {problems.map((c) => (
              <li key={c.slug}>
                <Link href={`/programisci/${encodeURIComponent(c.slug)}`} className={styles.link}>{c.namePl}</Link>
              </li>
            ))}
          </ul>
        ) : (
          <Alert variant="danger">{ts("home.categoriesUnavailable")}</Alert>
        )}
        <Link href="/programisci">{ts("home.allPractitioners")}</Link>
      </section>

      <section className={styles.section} aria-labelledby="home-steps">
        <h2 id="home-steps">{ts("home.stepsTitle")}</h2>
        <ol className={styles.steps}>
          {STEPS.map(([title, body]) => (
            <li key={title} className={styles.step}>
              <h3>{ts(title)}</h3>
              <p>{ts(body)}</p>
            </li>
          ))}
        </ol>
        <p><Link href="/jak-to-dziala">{ts("home.moreHow")}</Link></p>
      </section>

      <section className={styles.section} aria-labelledby="home-rules">
        <h2 id="home-rules">{ts("home.rulesTitle")}</h2>
        <p className={styles.prose}>{ts("home.rulesBody")}</p>
        <p>
          <Link href="/zasady-rankingu">{ts("home.rulesLink")}</Link>
          {" · "}
          <Link href="/cennik">{ts("home.pricingLink")}</Link>
        </p>
      </section>
    </>
  );
}
