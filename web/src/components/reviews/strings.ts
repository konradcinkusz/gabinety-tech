// Polish text of reviews; beside its components because the shared i18n/pl.ts belongs to T-07.02.
const text = {
  page: {
    formTitle: "Wystaw opinię",
    formMetaTitle: "Wystaw opinię | Gabinety Tech",
    listTitle: "Opinie o mnie",
    listMetaTitle: "Opinie o mnie | Gabinety Tech",
  },
  form: {
    intro: "Opinia dotyczy odbytej konsultacji. Można ją wystawić raz.",
    ratingLegend: "Ocena",
    ratingOption: "{n} z 5",
    textLabel: "Treść opinii",
    textHint: "Od 20 do 2000 znaków. Opisz, z czym przyszedłeś i jak przebiegła konsultacja.",
    submit: "Wyślij opinię",
    ratingRequired: "Wybierz ocenę od 1 do 5.",
    textTooShort: "Opinia musi mieć co najmniej 20 znaków.",
    textTooLong: "Opinia może mieć najwyżej 2000 znaków.",
    textInvalid: "Treść opinii nie została przyjęta. Popraw ją i spróbuj ponownie.",
    back: "Wróć do rezerwacji",
  },
  done: {
    title: "Dziękujemy za opinię",
    held: "Opinia czeka na sprawdzenie. Pojawi się na profilu programisty dopiero po zatwierdzeniu i może nie zostać opublikowana.",
    published: "Opinia została opublikowana na profilu programisty.",
  },
  already: {
    title: "Ta konsultacja ma już opinię",
    body: "Do jednej rezerwacji można wystawić tylko jedną opinię.",
  },
  notReviewable: {
    title: "Tej konsultacji nie można ocenić",
    body: "Opinię można wystawić po zakończonej konsultacji, w ciągu 14 dni od jej zakończenia.",
  },
  list: {
    loading: "Ładowanie opinii…",
    empty: "Nie masz jeszcze opublikowanych opinii.",
    noProfile: "Opinie widzą tu programiści z profilem. Najpierw utwórz profil w gabinecie programisty.",
    reviewer: "Zweryfikowany klient",
    rating: "Ocena: {n} z 5",
    more: "Pokaż więcej",
    replyHeading: "Twoja odpowiedź",
  },
  reply: {
    label: "Odpowiedź na opinię",
    hint: "Od 10 do 1000 znaków. Odpowiedzieć można raz, odpowiedzi nie da się później zmienić.",
    submit: "Wyślij odpowiedź",
    tooShort: "Odpowiedź musi mieć co najmniej 10 znaków.",
    already: "Na tę opinię już odpowiedziano.",
  },
  error: {
    network: "Brak połączenia. Sprawdź internet i spróbuj ponownie.",
    unavailable: "Usługa jest chwilowo niedostępna. Spróbuj za chwilę.",
    notFound: "Nie znaleziono tej rezerwacji.",
    forbidden: "Nie masz do tego uprawnień.",
    rateLimited: "Zbyt wiele prób. Spróbuj za chwilę.",
    validation: "Dane są nieprawidłowe. Sprawdź formularz.",
    generic: "Coś poszło nie tak. Spróbuj ponownie.",
  },
} as const;

type Flat = { [K in keyof typeof text]: `${K & string}.${keyof (typeof text)[K] & string}` }[keyof typeof text];
export type ReviewsTextKey = Flat;

export function mr(key: ReviewsTextKey, params?: Record<string, string | number>): string {
  const [group, name] = key.split(".") as [keyof typeof text, string];
  const raw = (text[group] as Record<string, string>)[name];
  return params ? raw.replace(/\{(\w+)\}/g, (whole, n: string) => (n in params ? String(params[n]) : whole)) : raw;
}
