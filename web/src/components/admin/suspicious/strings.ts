// Polish text of the suspicious-pairs page (SPEC section 5 rule 1). Kept beside the components because i18n/pl.ts belongs to T-07.02;
// it merges into that catalogue later (listed in the PR).
export const spl = {
  title: "Podejrzane pary",
  intro: "Pary klient–programista z więcej niż dwiema opłaconymi rezerwacjami i wyłącznie opiniami z oceną 5/5. To sygnał do sprawdzenia, nie dowód.",
  forbidden: "Nie masz dostępu do panelu administracyjnego.",
  unavailable: "Lista jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
  empty: "Brak podejrzanych par na tej stronie.",
  next: "Dalej",
  fact: {
    client: "Klient (id)",
    practitioner: "Programista (id)",
    bookings: "Opłacone rezerwacje",
    reviews: "Opinie (wszystkie 5/5)",
  },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];
export type SpKey = Leaves<typeof spl>;

/** Text of a key. */
export function sp(key: SpKey): string {
  let node: unknown = spl;
  for (const part of key.split(".")) node = (node as Record<string, unknown>)?.[part];
  return typeof node === "string" ? node : key;
}
