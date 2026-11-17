// Polish text of the disputes page (SPEC section 5 rule 1). Kept beside the components because i18n/pl.ts belongs to T-07.02;
// it merges into that catalogue later (listed in the PR).
export const dvl = {
  title: "Spory",
  forbidden: "Nie masz dostępu do panelu administracyjnego.",
  unavailable: "Lista sporów jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
  empty: { open: "Brak otwartych sporów.", resolved: "Brak rozstrzygniętych sporów." },
  next: "Więcej",
  filter: { label: "Status", open: "Otwarte", resolved: "Rozstrzygnięte" },
  kind: { not_as_described: "Usługa niezgodna z opisem", practitioner_no_show: "Nieobecność programisty", other: "Inny powód" },
  fact: {
    booking: "Rezerwacja",
    client: "Klient (id użytkownika)",
    practitioner: "Programista (id użytkownika)",
    opened: "Otwarto",
    resolved: "Rozstrzygnięto",
    text: "Treść zgłoszenia",
    outcome: "Rozstrzygnięcie",
    note: "Notatka moderatora",
  },
  resolution: { release: "Wypłata dla programisty", refund_full: "Pełny zwrot dla klienta", refund_partial: "Częściowy zwrot dla klienta" },
  resolve: {
    open: "Rozstrzygnij",
    legend: "Rozstrzygnięcie sporu",
    release: "Wypłać programiście",
    refund_full: "Zwróć klientowi całość",
    refund_partial: "Zwróć klientowi część",
    amount: "Kwota zwrotu w zł",
    amountHint: "Na przykład 12,50. Najwyżej dwa miejsca po przecinku.",
    note: "Notatka (10 do 500 znaków)",
    review: "Przejdź do potwierdzenia",
    confirm: "Potwierdź rozstrzygnięcie",
    back: "Wróć",
    cancel: "Anuluj",
    working: "Zapisywanie…",
    pick: "Wybierz rozstrzygnięcie.",
    noteInvalid: "Podaj notatkę: od 10 do 500 znaków.",
    amountFormat: "Podaj kwotę, na przykład 12,50.",
    amountDecimals: "Kwota może mieć najwyżej dwa miejsca po przecinku.",
    amountRange: "Kwota zwrotu musi być większa od zera i mniejsza od ceny rezerwacji.",
    amountRangeWith: "Kwota zwrotu musi być większa od zera i mniejsza od ceny ({price}).",
    confirmTitle: "Sprawdź skutki przed zatwierdzeniem. Tej decyzji nie można cofnąć.",
  },
  error: {
    stateChanged: "Stan się zmienił, odśwież stronę.",
    forbidden: "Nie masz uprawnień do tej akcji.",
    notFound: "Nie znaleziono rezerwacji.",
    refundMinor: "Kwota zwrotu jest poza dozwolonym zakresem.",
    note: "Notatka musi mieć od 10 do 500 znaków.",
    validation: "Serwer odrzucił dane rozstrzygnięcia.",
    unavailable: "Operator płatności jest chwilowo niedostępny. Spróbuj ponownie za chwilę.",
    generic: "Nie udało się zapisać rozstrzygnięcia. Spróbuj ponownie.",
    network: "Nie udało się połączyć z serwerem. Spróbuj ponownie.",
  },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];
export type DvKey = Leaves<typeof dvl>;

/** Text of a key; `{name}` placeholders are filled from `vars`. */
export function dv(key: DvKey | `confirm.${string}`, vars: Record<string, string> = {}): string {
  let node: unknown = key.startsWith("confirm.") ? confirm : dvl;
  for (const part of (key.startsWith("confirm.") ? key.slice(8) : key).split(".")) node = (node as Record<string, unknown>)?.[part];
  const text = typeof node === "string" ? node : key;
  return text.replace(/\{(\w+)\}/g, (_, k: string) => vars[k] ?? `{${k}}`);
}

// What the money does, in words (CONTRACTS 9.2). {price}, {refund}, {kept} are formatted amounts and may be unknown.
const confirm = {
  release: "Klient nie dostanie zwrotu. Programista otrzyma {price} pomniejszone o prowizję platformy (zasada pierwszej konsultacji).",
  refund_full: "Klient dostanie zwrot całości ({price}). Programista nie otrzyma nic, prowizji nie naliczamy.",
  partial: "Klient dostanie zwrot {refund}. Programista otrzyma resztę ({kept}) pomniejszoną o prowizję liczoną od tej reszty.",
  releaseNoPrice: "Klient nie dostanie zwrotu. Programista otrzyma całą cenę rezerwacji pomniejszoną o prowizję platformy (zasada pierwszej konsultacji).",
  refund_fullNoPrice: "Klient dostanie zwrot całej ceny rezerwacji. Programista nie otrzyma nic, prowizji nie naliczamy.",
  partialNoPrice: "Klient dostanie zwrot {refund}. Programista otrzyma resztę ceny rezerwacji pomniejszoną o prowizję liczoną od tej reszty.",
};
