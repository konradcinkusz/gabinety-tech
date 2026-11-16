// All user-facing Polish text lives here (SPEC section 5 rule 1).
export const pl = {
  site: { name: "Gabinety Tech", tagline: "Płatna konsultacja ze zweryfikowanym programistą" },
  nav: {
    label: "Menu główne",
    home: "Strona główna",
    search: "Programiści",
    howItWorks: "Jak to działa",
    rankingRules: "Zasady rankingu",
    pricing: "Cennik",
    signIn: "Zaloguj się",
    register: "Załóż konto",
    myBookings: "Moje rezerwacje",
    practitionerArea: "Panel programisty",
    admin: "Administracja",
    signOut: "Wyloguj się",
  },
  footer: {
    terms: "Regulamin",
    privacy: "Polityka prywatności",
    copyright: "© {year} Gabinety Tech",
  },
  common: {
    save: "Zapisz",
    cancel: "Anuluj",
    back: "Wstecz",
    next: "Dalej",
    previous: "Poprzednia",
    loading: "Ładowanie",
    search: "Szukaj",
    yes: "Tak",
    no: "Nie",
    required: "wymagane",
    optional: "opcjonalne",
    close: "Zamknij",
    characterCount: "{count} / {max}",
  },
  errors: {
    genericTitle: "Coś poszło nie tak",
    genericBody: "Wystąpił nieoczekiwany błąd. Spróbuj ponownie za chwilę.",
    notFoundTitle: "Nie znaleziono strony",
    notFoundBody: "Strona, której szukasz, nie istnieje lub została przeniesiona.",
    retry: "Spróbuj ponownie",
    network: "Brak połączenia z siecią. Sprawdź połączenie i spróbuj ponownie.",
  },
  a11y: {
    skipToContent: "Przejdź do treści",
    spinnerLabel: "Ładowanie",
    pagination: "Paginacja",
    previousPage: "Poprzednia strona",
    nextPage: "Następna strona",
  },
  currency: { pln: "zł" },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];

export type MessageKey = Leaves<typeof pl>;

function flatten(node: object, prefix = ""): string[] {
  return Object.entries(node).flatMap(([k, v]) =>
    typeof v === "string" ? [`${prefix}${k}`] : flatten(v as object, `${prefix}${k}.`),
  );
}

export const messageKeys = flatten(pl) as MessageKey[];

export function t(key: MessageKey, params?: Record<string, string | number>): string {
  let node: unknown = pl;
  for (const part of key.split(".")) node = (node as Record<string, unknown>)[part];
  const text = typeof node === "string" ? node : key;
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in params ? String(params[name]) : whole,
  );
}
