// Polish text of the verification queue (SPEC section 5 rule 1). Kept beside the components because i18n/pl.ts belongs to T-07.02;
// it merges into that catalogue later (listed in the PR).
export const avl = {
  nav: { label: "Administracja", verification: "Weryfikacja programistów", disputes: "Spory", ledger: "Księga", reviews: "Opinie" },
  title: "Weryfikacja programistów",
  forbidden: "Nie masz dostępu do panelu administracyjnego.",
  unavailable: "Kolejka jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
  empty: "Brak programistów w tym statusie.",
  next: "Starsze wpisy dalej",
  filter: { label: "Status", Submitted: "Czeka na weryfikację", Approved: "Zatwierdzeni", Suspended: "Zawieszeni", Rejected: "Odrzuceni" },
  fact: {
    github: "GitHub",
    githubLinked: "powiązany",
    githubNot: "niepowiązany",
    nip: "NIP",
    nipValid: "poprawny",
    nipInvalid: "niepoprawny lub brak",
    submitted: "Zgłoszono",
    created: "Utworzono",
    reason: "Powód",
    none: "brak",
    slug: "Adres profilu",
    bio: "Opis",
    portfolio: "Portfolio",
    updated: "Zmieniono",
    approved: "Zatwierdzono",
  },
  status: { Draft: "Szkic", Submitted: "Czeka na weryfikację", Approved: "Zatwierdzony", Rejected: "Odrzucony", Suspended: "Zawieszony" },
  action: {
    approve: "Zatwierdź",
    reject: "Odrzuć",
    suspend: "Zawieś",
    reinstate: "Przywróć",
    confirmReject: "Potwierdź odrzucenie",
    confirmSuspend: "Potwierdź zawieszenie",
    cancel: "Anuluj",
    working: "Zapisywanie…",
  },
  detail: { title: "Szczegóły programisty", open: "Szczegóły", data: "Dane programisty", back: "Wróć do kolejki", unavailable: "Dane programisty są chwilowo niedostępne. Spróbuj ponownie za chwilę." },
  timeline: {
    title: "Historia weryfikacji",
    empty: "Brak zdarzeń weryfikacji.",
    unavailable: "Historia weryfikacji jest chwilowo niedostępna.",
    actor: "Wykonał (id)",
    note: "Notatka",
  },
  event: {
    Submitted: "Zgłoszono do weryfikacji",
    Approved: "Zatwierdzono",
    Rejected: "Odrzucono",
    Suspended: "Zawieszono",
    Reinstated: "Przywrócono",
    GithubLinked: "Powiązano konto GitHub",
    NipChanged: "Zmieniono NIP",
    unknown: "Inne zdarzenie",
  },
  reason: { label: "Powód (10 do 500 znaków)", invalid: "Podaj powód: od 10 do 500 znaków." },
  error: {
    stateChanged: "Status się zmienił, odśwież stronę.",
    forbidden: "Nie masz uprawnień do tej akcji.",
    notFound: "Nie znaleziono programisty.",
    generic: "Nie udało się wykonać akcji. Spróbuj ponownie.",
    network: "Nie udało się połączyć z serwerem. Spróbuj ponownie.",
  },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];
export type AvKey = Leaves<typeof avl>;

export function av(key: AvKey): string {
  let node: unknown = avl;
  for (const part of key.split(".")) node = (node as Record<string, unknown>)[part];
  return typeof node === "string" ? node : key;
}
