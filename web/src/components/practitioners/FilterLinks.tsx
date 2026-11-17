import Link from "next/link";
import { Button, TextInput } from "@/components/ui";
import { t } from "@/i18n/pl";
import { DURATIONS, PRICE_PRESETS, listHref, listPath, withFilter, type ListState } from "@/lib/seo/listUrl";
import { formatPln } from "@/components/ui";
import type { Category } from "@/lib/seo/publicApi";
import styles from "./practitioners.module.css";
import { tp } from "./strings";

type Item = { label: string; href: string; current: boolean };

function Group({ title, items }: { title: string; items: Item[] }) {
  return (
    <section className={styles.group}>
      <h2>{title}</h2>
      <ul className={styles.links}>
        {items.map((i) => (
          <li key={i.href}>
            <Link href={i.href} className={styles.link} aria-current={i.current ? "true" : undefined}>{i.label}</Link>
          </li>
        ))}
      </ul>
    </section>
  );
}

/** Filters as plain links (no JavaScript); each link keeps the other filters and returns to the first page. */
export function FilterLinks({ state, problems, techs }: { state: ListState; problems: Category[]; techs: Category[] }) {
  const problemItems: Item[] = [
    { label: tp("filters.any"), href: listHref(withFilter(state, { problem: undefined, tech: undefined })), current: !state.problem },
    ...problems.map((c) => ({ label: c.namePl, href: listHref(withFilter(state, { problem: c.slug })), current: state.problem === c.slug })),
  ];
  const techItems: Item[] = state.problem
    ? [
        { label: tp("filters.any"), href: listHref(withFilter(state, { tech: undefined })), current: !state.tech },
        ...techs.map((c) => ({ label: c.namePl, href: listHref(withFilter(state, { tech: c.slug })), current: state.tech === c.slug })),
      ]
    : [];
  const durationItems: Item[] = [
    { label: tp("filters.any"), href: listHref(withFilter(state, { duration: undefined })), current: !state.duration },
    ...DURATIONS.map((d) => ({ label: tp("filters.minutes", { n: d }), href: listHref(withFilter(state, { duration: d })), current: state.duration === d })),
  ];
  const priceItems: Item[] = [
    { label: tp("filters.anyPrice"), href: listHref(withFilter(state, { maxPriceMinor: undefined })), current: !state.maxPriceMinor },
    ...PRICE_PRESETS.map((p) => ({
      label: tp("filters.upTo", { price: formatPln(p) }),
      href: listHref(withFilter(state, { maxPriceMinor: p })),
      current: state.maxPriceMinor === p,
    })),
  ];
  return (
    <nav aria-label={tp("filters.label")} className={styles.filters}>
      <Group title={tp("filters.problem")} items={problemItems} />
      {techItems.length > 0 && <Group title={tp("filters.tech")} items={techItems} />}
      <Group title={tp("filters.duration")} items={durationItems} />
      <Group title={tp("filters.price")} items={priceItems} />
      {/* A plain GET form: works without JavaScript and keeps the other filters as hidden fields. */}
      <form method="get" action={listPath(state)} role="search" className={styles.search}>
        {state.duration && <input type="hidden" name="duration" value={state.duration} />}
        {state.maxPriceMinor && <input type="hidden" name="maxPriceMinor" value={state.maxPriceMinor} />}
        <TextInput name="q" type="search" maxLength={60} defaultValue={state.q} aria-label={tp("filters.searchLabel")} placeholder={tp("filters.searchPlaceholder")} />
        <Button type="submit">{t("common.search")}</Button>
      </form>
    </nav>
  );
}
