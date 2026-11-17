// Polish text of the review moderation page (SPEC section 5 rule 1). Kept beside the components because i18n/pl.ts belongs to T-07.02;
// it merges into that catalogue later (listed in the PR).
export const mvl = {
  title: "Opinie do moderacji",
  forbidden: "Nie masz dostępu do panelu administracyjnego.",
  unavailable: "Lista opinii jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
  empty: { Held: "Brak opinii oczekujących na sprawdzenie.", Published: "Brak opublikowanych opinii.", Rejected: "Brak odrzuconych opinii." },
  next: "Starsze wpisy dalej",
  filter: { label: "Status", Held: "Wstrzymane", Published: "Opublikowane", Rejected: "Odrzucone" },
  status: { Held: "Wstrzymana", Published: "Opublikowana", Rejected: "Odrzucona" },
  fact: {
    rating: "Ocena",
    created: "Dodano",
    published: "Opublikowano",
    practitioner: "Programista (id)",
    triage: "Wstępna ocena asystenta",
    reasons: "Powody wstrzymania",
    text: "Treść opinii",
    note: "Powód odrzucenia",
    moderator: "Moderator (id)",
  },
  ratingOf: "{n}/5",
  triage: {
    none: "Bez zastrzeżeń",
    hold: "Wstrzymana przez asystenta",
    unavailable: "Asystent był niedostępny, opinię opublikowano bez wstępnej oceny",
    unknown: "Brak informacji",
  },
  reason: {
    spam: "Spam",
    abuse: "Obraźliwa treść",
    personal_data: "Dane osobowe",
    off_topic: "Niezwiązana z tematem",
    threat: "Groźby",
    other: "Inny powód",
    none: "Brak podanych powodów",
  },
  publish: { open: "Opublikuj", working: "Publikowanie…" },
  reject: {
    open: "Odrzuć",
    label: "Powód odrzucenia (1 do 500 znaków)",
    confirm: "Potwierdź odrzucenie",
    cancel: "Anuluj",
    working: "Zapisywanie…",
    reasonInvalid: "Podaj powód: od 1 do 500 znaków.",
    warning: "Odrzucenie usuwa opinię z profilu programisty.",
  },
  error: {
    stateChanged: "Stan opinii się zmienił, lista została odświeżona.",
    forbidden: "Nie masz uprawnień do tej akcji.",
    notFound: "Nie znaleziono opinii.",
    reason: "Serwer odrzucił powód: musi mieć od 1 do 500 znaków.",
    unavailable: "Usługa opinii jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
    generic: "Nie udało się zapisać decyzji. Spróbuj ponownie.",
    network: "Nie udało się połączyć z serwerem. Spróbuj ponownie.",
  },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];
export type MvKey = Leaves<typeof mvl>;

/** Text of a key; `{name}` placeholders are filled from `vars`. */
export function mv(key: MvKey, vars: Record<string, string> = {}): string {
  let node: unknown = mvl;
  for (const part of key.split(".")) node = (node as Record<string, unknown>)?.[part];
  const text = typeof node === "string" ? node : key;
  return text.replace(/\{(\w+)\}/g, (_, k: string) => vars[k] ?? `{${k}}`);
}
