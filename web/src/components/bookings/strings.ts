// Polish text of "my bookings"; beside its components because the shared i18n/pl.ts belongs to T-07.02.
const text = {
  page: {
    title: "Moje rezerwacje",
    metaTitle: "Moje rezerwacje | Gabinety Tech",
    detailTitle: "Szczegóły rezerwacji",
    detailMetaTitle: "Szczegóły rezerwacji | Gabinety Tech",
  },
  role: {
    label: "Pokaż rezerwacje jako",
    client: "Klient",
    practitioner: "Programista",
  },
  list: {
    statusLabel: "Status",
    allStatuses: "Wszystkie",
    loading: "Ładowanie rezerwacji…",
    empty: "Brak rezerwacji w tym widoku.",
    more: "Pokaż więcej",
    details: "Szczegóły",
  },
  party: {
    practitioner: "Programista",
    client: "Klient",
  },
  status: {
    PendingPayment: "Oczekuje na płatność",
    Confirmed: "Potwierdzona",
    CancelledByClient: "Anulowana przez klienta",
    LateCancelledByClient: "Anulowana przez klienta (bez zwrotu)",
    CancelledByPractitioner: "Anulowana przez programistę",
    Expired: "Wygasła",
    Completed: "Zakończona",
    ClientNoShow: "Klient nie stawił się",
    Disputed: "Spór w toku",
    Resolved: "Spór rozstrzygnięty",
    unknown: "Status nieznany",
  },
  payment: {
    Created: "Płatność rozpoczęta",
    Succeeded: "Opłacona",
    Failed: "Płatność nieudana",
    Refunded: "Zwrócona w całości",
    PartiallyRefunded: "Zwrócona częściowo",
    unknown: "Stan płatności nieznany",
  },
  detail: {
    loading: "Ładowanie rezerwacji…",
    when: "Termin",
    duration: "Czas trwania",
    minutes: "{n} min",
    zone: "Godziny w czasie polskim (Europe/Warsaw).",
    status: "Status",
    price: "Cena",
    payment: "Płatność",
    refunded: "Zwrócono: {amount}",
    fee: "Prowizja",
    payout: "Wypłata dla programisty",
    brief: "Opis problemu (klient)",
    briefOwn: "Twój opis problemu",
    meeting: "Link do spotkania",
    back: "Wróć do listy rezerwacji",
    review: "Wystaw opinię",
  },
  actions: {
    cancel: "Anuluj rezerwację",
    dispute: "Zgłoś spór",
    noShow: "Zgłoś nieobecność klienta",
  },
  cancel: {
    title: "Czy na pewno anulować rezerwację?",
    clientRule: "Zwrot zależy od terminu: jeśli do rozpoczęcia konsultacji zostało co najmniej 24 godziny, zwrot wynosi 100% ceny; jeśli mniej niż 24 godziny, brak zwrotu.",
    practitionerRule: "Jako programista anulujesz zawsze z pełnym zwrotem: klient dostanie z powrotem 100% ceny, a Ty nie otrzymasz wypłaty za tę konsultację.",
    reasonLabel: "Powód (opcjonalnie)",
    reasonHint: "Najwyżej 500 znaków.",
    confirm: "Tak, anuluj rezerwację",
    back: "Nie anuluj",
    done: "Rezerwacja została anulowana.",
  },
  dispute: {
    title: "Zgłoszenie sporu",
    kindLabel: "Rodzaj sporu",
    kind_not_as_described: "Konsultacja różniła się od opisu",
    kind_practitioner_no_show: "Programista się nie stawił",
    kind_other: "Inny powód",
    textLabel: "Opis sporu",
    textHint: "Od 10 do 1000 znaków.",
    tooShort: "Opis musi mieć co najmniej 10 znaków.",
    tooLong: "Opis może mieć najwyżej 1000 znaków.",
    submit: "Wyślij zgłoszenie",
    cancel: "Zamknij",
    done: "Spór został zgłoszony. Wypłata dla programisty jest wstrzymana do rozstrzygnięcia.",
  },
  noShow: {
    title: "Zgłosić nieobecność klienta?",
    body: "Konsultacja zostanie rozliczona jak odbyta. Tego nie można cofnąć.",
    confirm: "Tak, zgłoś nieobecność",
    back: "Nie zgłaszaj",
    done: "Nieobecność klienta została zgłoszona.",
  },
  error: {
    invalidTransition: "To nie jest już możliwe: stan rezerwacji się zmienił. Odśwież stronę.",
    refresh: "Odśwież",
    notFound: "Nie znaleziono takiej rezerwacji.",
    unavailable: "Usługa rezerwacji jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
    validation: "Sprawdź wypełnione pola i spróbuj ponownie.",
    forbidden: "Nie masz dostępu do tej rezerwacji.",
    rateLimited: "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.",
    network: "Nie udało się połączyć z usługą. Spróbuj ponownie.",
    generic: "Coś poszło nie tak. Spróbuj ponownie.",
    reason: "Powód jest nieprawidłowy (najwyżej 500 znaków).",
    kind: "Wybierz rodzaj sporu.",
    field: "To pole jest nieprawidłowe.",
  },
} as const;

type Flat = { [K in keyof typeof text]: `${K & string}.${keyof (typeof text)[K] & string}` }[keyof typeof text];
export type BookingsTextKey = Flat;

export function mb(key: BookingsTextKey, params?: Record<string, string | number>): string {
  const [group, name] = key.split(".") as [keyof typeof text, string];
  const raw = (text[group] as Record<string, string>)[name];
  return params ? raw.replace(/\{(\w+)\}/g, (whole, n: string) => (n in params ? String(params[n]) : whole)) : raw;
}
