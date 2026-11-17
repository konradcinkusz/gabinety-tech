// Polish text of the practitioner list pages. It sits beside its components only because the shared module
// web/src/i18n/pl.ts is outside this task's files; the PR lists "merge into pl.ts" as an out-of-scope follow-up.
// Same {name} interpolation as t() in i18n/pl.ts.
const text = {
  list: {
    allTitle: "Programiści",
    allIntro: "Zweryfikowani programiści, z którymi umówisz płatną konsultację online. Wybierz problem, technologię i czas trwania.",
    categoryIntro: "Konsultacje „{name}” ze zweryfikowanymi programistami. Wybierz technologię, czas i budżet, a potem zarezerwuj termin.",
    categoryTechIntro: "Konsultacje „{name}” ze zweryfikowanymi programistami. Przeglądasz wyniki zawężone do wybranej technologii.",
    count: "Wyniki: {count}",
    empty: "Brak programistów dla wybranych filtrów.",
    clearFilters: "Wyczyść filtry",
    resultsLabel: "Lista programistów",
    firstPage: "Pierwsza strona wyników",
  },
  meta: {
    allTitle: "Programiści do konsultacji online | Gabinety Tech",
    allDescription: "Przeglądaj zweryfikowanych programistów i umów płatną konsultację online: audyt kodu, migracja, integracja AI i więcej.",
    problemTitle: "{problem}: konsultacje z programistą | Gabinety Tech",
    problemDescription: "{problem}: umów płatną konsultację ze zweryfikowanym programistą. Dostępnych programistów: {count}.",
    techTitle: "{problem} i {tech}: konsultacje z programistą | Gabinety Tech",
    techDescription: "{problem} w technologii {tech}: umów płatną konsultację ze zweryfikowanym programistą. Dostępnych programistów: {count}.",
  },
  filters: {
    label: "Filtry",
    problem: "Problem",
    tech: "Technologia",
    duration: "Czas trwania",
    price: "Cena do",
    any: "Wszystkie",
    anyPrice: "Dowolna",
    minutes: "{n} min",
    upTo: "do {price}",
    searchLabel: "Szukaj po imieniu lub nagłówku",
    searchPlaceholder: "np. migracja",
  },
  card: {
    from: "od",
    isNew: "Nowy",
    rating: "Ocena {average} ({count})",
    durations: "Czas konsultacji",
    badgeEmail: "E-mail zweryfikowany",
    badgeGithub: "GitHub połączony",
    badgeNip: "NIP zweryfikowany",
    badgeApproved: "Zatwierdzony",
  },
} as const;

export type TextKey = {
  [G in keyof typeof text]: `${G}.${keyof (typeof text)[G] & string}`;
}[keyof typeof text];

export function tp(key: TextKey, params?: Record<string, string | number>): string {
  const [group, name] = key.split(".") as [keyof typeof text, string];
  const raw = (text[group] as Record<string, string>)[name];
  return params ? raw.replace(/\{(\w+)\}/g, (whole, n: string) => (n in params ? String(params[n]) : whole)) : raw;
}
