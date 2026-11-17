// Polish text of the ledger page (SPEC section 5 rule 1). Kept beside the components because i18n/pl.ts belongs to T-07.02;
// it merges into that catalogue later (listed in the PR).
export const lgl = {
  title: "Księga",
  forbidden: "Nie masz dostępu do panelu administracyjnego.",
  intro: "Eksport wpisów księgi do pliku CSV (kolumny: rezerwacja, rodzaj, kwota w groszach, waluta, data). Daty to dni UTC, obie granice włącznie.",
  filter: { label: "Okres", from: "Od", to: "Do", show: "Pokaż okres" },
  preset: { label: "Szybki wybór", thisMonth: "Ten miesiąc", lastMonth: "Poprzedni miesiąc", thisYear: "Ten rok" },
  download: "Pobierz CSV",
  summary: "Wybrany okres: {from} – {to}.",
  problem: {
    format: "Podaj obie daty w formacie rrrr-mm-dd.",
    order: "Data końcowa nie może być wcześniejsza niż początkowa.",
    range: "Okres może mieć najwyżej 366 dni.",
  },
  error: {
    unauthorized: "Sesja wygasła. Zaloguj się ponownie.",
    forbidden: "Nie masz uprawnień do pobrania księgi.",
    validation: "Serwer odrzucił okres. Sprawdź daty.",
    unavailable: "Księga jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
  },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];
export type LgKey = Leaves<typeof lgl>;

/** Text of a key; `{name}` placeholders are filled from `vars`. */
export function lg(key: LgKey, vars: Record<string, string> = {}): string {
  let node: unknown = lgl;
  for (const part of key.split(".")) node = (node as Record<string, unknown>)?.[part];
  const text = typeof node === "string" ? node : key;
  return text.replace(/\{(\w+)\}/g, (_, k: string) => vars[k] ?? `{${k}}`);
}
