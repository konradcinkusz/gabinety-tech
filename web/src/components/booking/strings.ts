// Polish text of the booking flow; beside its components because the shared i18n/pl.ts belongs to T-07.02.
const text = {
  page: {
    title: "Rezerwacja konsultacji",
    metaTitle: "Rezerwacja konsultacji | Gabinety Tech",
    missingSlot: "Nie wybrano terminu albo adres jest niepełny. Wybierz termin na stronie gabinetu.",
    backToSearch: "Wróć do wyszukiwarki",
  },
  summary: {
    title: "Podsumowanie",
    offering: "Konsultacja",
    practitioner: "Programista",
    when: "Termin",
    duration: "Czas trwania",
    minutes: "{n} min",
    price: "Do zapłaty",
    zone: "Godziny w czasie polskim (Europe/Warsaw). Termin jest wstrzymany na 10 minut od kliknięcia „Zapłać”.",
  },
  brief: {
    label: "Opisz problem",
    hint: "Od 20 do 2000 znaków. Opis zobaczy programista po opłaceniu rezerwacji.",
    tooShort: "Opis musi mieć co najmniej 20 znaków.",
    tooLong: "Opis może mieć najwyżej 2000 znaków.",
  },
  assist: {
    ask: "Pomóż mi opisać problem",
    asking: "Przygotowuję propozycję…",
    note: "Propozycja to tylko podpowiedź: możesz ją zmienić i nic nie zostanie dodane bez Twojego kliknięcia.",
    proposal: "Propozycja opisu (możesz ją edytować)",
    questions: "Warto jeszcze opisać:",
    category: "Sugerowana kategoria: {slug}",
    apply: "Dodaj propozycję do opisu",
    applied: "Propozycja została dodana do opisu.",
    unavailable: "Asystent jest chwilowo niedostępny. Opisz problem samodzielnie i zarezerwuj termin.",
    rateLimited: "Zbyt wiele próśb do asystenta. Spróbuj za chwilę albo opisz problem samodzielnie.",
    failed: "Asystent nie mógł przygotować propozycji. Opisz problem samodzielnie.",
  },
  form: {
    pay: "Zapłać",
    paying: "Przetwarzanie…",
  },
  error: {
    unavailable: "Nie udało się połączyć z usługą. Spróbuj ponownie: nie zostaniesz obciążony dwa razy.",
    rateLimited: "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.",
    slotTaken: "Ten termin został już zajęty albo nie jest dostępny. Wybierz inny termin.",
    inactive: "Ta konsultacja nie jest już dostępna.",
    own: "Nie możesz zarezerwować własnej konsultacji.",
    notBookable: "Ten programista nie przyjmuje teraz rezerwacji.",
    generic: "Nie udało się utworzyć rezerwacji. Sprawdź dane i spróbuj ponownie.",
    toProfile: "Wróć do profilu i wybierz termin",
  },
  checkout: {
    title: "Płatność (tryb demonstracyjny)",
    intro: "To symulacja płatności: żadne pieniądze nie są pobierane.",
    hold: "Termin jest wstrzymany jeszcze przez {time}",
    expired: "Czas na płatność minął, termin został zwolniony. Wybierz termin ponownie.",
    succeed: "Symuluj udaną płatność",
    fail: "Symuluj nieudaną płatność",
    failed: "Płatność nie powiodła się. Rezerwacja nie została potwierdzona.",
    notMock: "Symulacja płatności jest wyłączona w tym środowisku.",
    closed: "Ta rezerwacja nie oczekuje już na płatność.",
    toBooking: "Zobacz rezerwację",
    loading: "Ładowanie rezerwacji…",
    loadFailed: "Nie udało się wczytać rezerwacji. Odśwież stronę.",
    error: "Symulacja nie powiodła się. Spróbuj ponownie.",
    toProfile: "Wróć do profilu",
  },
  confirmation: {
    title: "Rezerwacja",
    confirmed: "Rezerwacja potwierdzona. Dziękujemy!",
    pending: "Czekamy na potwierdzenie płatności.",
    other: "Ta rezerwacja nie jest aktywna.",
    meeting: "Link do spotkania",
    refresh: "Odśwież",
    toSearch: "Wróć do wyszukiwarki",
  },
} as const;

type Flat = { [K in keyof typeof text]: `${K & string}.${keyof (typeof text)[K] & string}` }[keyof typeof text];
export type BookingTextKey = Flat;

export function bk(key: BookingTextKey, params?: Record<string, string | number>): string {
  const [group, name] = key.split(".") as [keyof typeof text, string];
  const raw = (text[group] as Record<string, string>)[name];
  return params ? raw.replace(/\{(\w+)\}/g, (whole, n: string) => (n in params ? String(params[n]) : whole)) : raw;
}
