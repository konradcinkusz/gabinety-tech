// Polish text of the public practitioner page; beside its components because the shared i18n/pl.ts is outside this task's files
// (the PR lists "merge into pl.ts" as an out-of-scope follow-up). Same {name} interpolation as t() in i18n/pl.ts.
const text = {
  page: {
    titleSuffix: "Gabinety Tech",
    metaTitle: "{name}: konsultacje online | Gabinety Tech",
    metaDescription: "{name}. {headline} Umów płatną konsultację online.",
    offeringsTitle: "Oferta",
    aboutTitle: "O mnie",
    bookingTitle: "Wolne terminy",
    duration: "{n} min",
    book: "Zarezerwuj",
    bookOffering: "Zarezerwuj: {title}",
    portfolio: "Portfolio",
    github: "GitHub",
    completed: "Zrealizowane konsultacje: {count}",
    memberSince: "Na platformie od {date}",
    isNew: "Nowy",
    rating: "Ocena {average} ({count})",
    badgesLabel: "Weryfikacje",
    noOfferings: "Ten programista nie ma teraz aktywnych ofert.",
  },
  badge: {
    email: "E-mail zweryfikowany",
    emailInfo: "Adres e-mail tego programisty został potwierdzony.",
    github: "GitHub połączony",
    githubInfo: "Konto GitHub należy do tego programisty: potwierdził to kodem w publicznym profilu.",
    nip: "NIP zweryfikowany",
    nipInfo: "Numer NIP działalności tego programisty został sprawdzony.",
    approved: "Zatwierdzony",
    approvedInfo: "Profil został sprawdzony i zatwierdzony przez zespół Gabinety Tech.",
  },
} as const;

export type ProfileTextKey = {
  [G in keyof typeof text]: `${G}.${keyof (typeof text)[G] & string}`;
}[keyof typeof text];

export function pf(key: ProfileTextKey, params?: Record<string, string | number>): string {
  const [group, name] = key.split(".") as [keyof typeof text, string];
  const raw = (text[group] as Record<string, string>)[name];
  return params ? raw.replace(/\{(\w+)\}/g, (whole, n: string) => (n in params ? String(params[n]) : whole)) : raw;
}
