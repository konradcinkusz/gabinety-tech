// Polish text of the sign-in, registration and account pages (SPEC section 5 rule 1).
// Kept beside the components because i18n/pl.ts belongs to T-07.02; it merges into that catalogue later.
export const authPl = {
  login: {
    title: "Logowanie",
    email: "Adres e-mail",
    password: "Hasło",
    submit: "Zaloguj się",
    submitting: "Logowanie…",
    invalidCredentials: "Nieprawidłowy adres e-mail lub hasło.",
    verificationRequired: "Potwierdź adres e-mail linkiem z wiadomości, a potem zaloguj się.",
    twoFactor: "Konta z uwierzytelnianiem dwuskładnikowym nie są jeszcze obsługiwane.",
    rateLimited: "Zbyt wiele prób logowania. Spróbuj ponownie za minutę.",
    unavailable: "Logowanie jest chwilowo niedostępne. Spróbuj ponownie za chwilę.",
    githubFailed: "Logowanie przez GitHub nie powiodło się. Spróbuj ponownie lub użyj hasła.",
    noAccount: "Nie masz konta?",
    github: "Zaloguj się przez GitHub",
    or: "albo",
  },
  register: {
    title: "Załóż konto",
    email: "Adres e-mail",
    password: "Hasło",
    passwordHint: "Co najmniej 8 znaków, w tym mała i wielka litera, cyfra i znak specjalny.",
    consent: "Akceptuję Regulamin (wersja {terms}) i Politykę prywatności (wersja {privacy}).",
    consentRequired: "Aby założyć konto, zaakceptuj Regulamin i Politykę prywatności.",
    passwordTooShort: "Hasło musi mieć co najmniej 8 znaków.",
    passwordWeak: "Hasło musi zawierać małą i wielką literę, cyfrę i znak specjalny.",
    rateLimited: "Zbyt wiele prób. Spróbuj ponownie za minutę.",
    submit: "Załóż konto",
    failed: "Nie udało się założyć konta. Sprawdź dane i spróbuj ponownie.",
    checkInbox: "Wysłaliśmy wiadomość z linkiem potwierdzającym. Po potwierdzeniu adresu możesz się zalogować.",
    unavailable: "Rejestracja jest chwilowo niedostępna. Spróbuj ponownie za chwilę.",
    haveAccount: "Masz już konto?",
    terms: "Regulamin",
    privacy: "Polityka prywatności",
  },
  account: {
    title: "Twoje konto",
    email: "Adres e-mail",
    verified: "Adres e-mail potwierdzony",
    notVerified: "Adres e-mail niepotwierdzony. Sprawdź skrzynkę i kliknij link z wiadomości.",
    consents: "Zgody",
    termsAccepted: "Regulamin: zaakceptowany (wersja {version})",
    termsPending: "Regulamin: wymaga ponownej akceptacji (wersja {version})",
    privacyAccepted: "Polityka prywatności: zaakceptowana (wersja {version})",
    privacyPending: "Polityka prywatności: wymaga ponownej akceptacji (wersja {version})",
    acceptCurrent: "Zaakceptuj aktualne wersje",
    data: "Twoje dane",
    export: "Pobierz moje dane (JSON)",
    deleteTitle: "Usunięcie konta",
    deleteBody: "Konto zostanie usunięte, a dane po okresie przechowywania zanonimizowane. Tej operacji nie da się cofnąć.",
    deleteConfirm: "Rozumiem i chcę usunąć konto",
    deletePassword: "Hasło (potwierdzenie)",
    deleteSubmit: "Usuń konto",
    deleteFailed: "Nie udało się usunąć konta. Sprawdź hasło i spróbuj ponownie.",
    deleteConfirmRequired: "Zaznacz potwierdzenie, aby usunąć konto.",
    unavailable: "Dane konta są chwilowo niedostępne. Spróbuj ponownie za chwilę.",
  },
} as const;

type Leaves<T, P extends string = ""> = {
  [K in keyof T & string]: T[K] extends string ? `${P}${K}` : Leaves<T[K], `${P}${K}.`>;
}[keyof T & string];
export type AuthKey = Leaves<typeof authPl>;

export function at(key: AuthKey, params?: Record<string, string | number>): string {
  let node: unknown = authPl;
  for (const part of key.split(".")) node = (node as Record<string, unknown>)[part];
  const text = typeof node === "string" ? node : key;
  return params ? text.replace(/\{(\w+)\}/g, (w, n: string) => (n in params ? String(params[n]) : w)) : text;
}
