namespace Gabinety.Web.Text;

/// <summary>
/// All user-facing Polish text of the web host, in one place (SPEC 5 rule 1; the Next.js app has <c>web/src/i18n/pl.ts</c> and the per-feature <c>strings.ts</c>). No Polish literal in a
/// component or a class outside this file: a test reads the sources for it. Strings copied from the Next.js app are verbatim, so the journeys (which find things by role and name)
/// run against either host.
/// </summary>
public static class Pl
{
    public static class Site
    {
        public const string Name = "Gabinety Tech", Tagline = "Płatna konsultacja ze zweryfikowanym programistą";
        public const string Copyright = "© {0} Gabinety Tech";
        public const string Terms = "Regulamin", Privacy = "Polityka prywatności", FooterLabel = "Informacje prawne";
        // UX-010: the footer's column titles (the legal column is named by FooterLabel for assistive technology).
        public const string ProductColumn = "Produkt", PractitionersColumn = "Dla programistów", LegalColumn = "Prawne";
    }

    public static class Nav
    {
        public const string Label = "Menu główne", Search = "Programiści", SignIn = "Zaloguj się", Register = "Załóż konto", MyBookings = "Moje rezerwacje", SignOut = "Wyloguj się";
        public const string Account = "Konto", PractitionerArea = "Panel programisty", Admin = "Administracja", Menu = "Menu", AccountMenu = "Twoje konto";
        // UX-010: the public links of the header and footer, and the account menu's entry for a signed-in user without a practitioner profile (D-2).
        public const string HowItWorks = "Jak to działa", Pricing = "Cennik", RankingRules = "Zasady rankingu", BecomePractitioner = "Zostań programistą";
    }

    public static class A11y
    {
        public const string SkipToContent = "Przejdź do treści", Flash = "Komunikaty", Pagination = "Paginacja", DismissFlash = "Zamknij komunikat", PreviousPage = "Poprzednia strona", NextPage = "Następna strona";
        /// <summary>Read before a field error by a screen reader (the error text itself is not colour only).</summary>
        public const string ErrorPrefix = "Błąd:";
    }

    public static class Errors
    {
        public const string GenericTitle = "Coś poszło nie tak", GenericBody = "Wystąpił nieoczekiwany błąd. Spróbuj ponownie za chwilę.";
        public const string NotFoundTitle = "Nie znaleziono strony", NotFoundBody = "Strona, której szukasz, nie istnieje lub została przeniesiona.";
        public const string Reconnecting = "Łączenie z serwerem…", ReconnectFailed = "Utracono połączenie z serwerem. Odśwież stronę.";
        // V-12: the 404 search and category links (DESIGN-CONTRACT 5.10).
        public const string NotFoundSearchRegion = "Wyszukiwarka programistów", NotFoundSearchLabel = "Czego szukasz?", NotFoundSearchButton = "Szukaj";
        // V-12: the error page's way back and retry.
        public const string RetryLink = "Spróbuj ponownie", HomeLink = "Wróć na stronę główną";
    }

    /// <summary>Flash messages are looked up by a fixed key (<c>?flash=signed-out</c>); text from a query string is never rendered.</summary>
    public static class Flash
    {
        public const string SignedOut = "Wylogowano.";
    }

    public static class Home
    {
        public const string MetaTitle = "Gabinety Tech: płatna konsultacja ze zweryfikowanym programistą";
        public const string MetaDescription = "Opisz problem, wybierz zweryfikowanego programistę i umów płatną konsultację online. Ranking bez płatnych miejsc.";
        public const string Title = "Umów płatną konsultację ze zweryfikowanym programistą";
        public const string Lead = "Audyt kodu, migracja, integracja AI: wybierz problem, a potem programistę, którego profil, ceny i opinie możesz sprawdzić przed rezerwacją.";
        public const string SearchLabel = "Czego szukasz?", SearchPlaceholder = "np. migracja bazy danych", SearchButton = "Szukaj", SearchRegion = "Wyszukiwarka programistów";
        public const string CategoriesTitle = "Wybierz problem", CategoriesLabel = "Kategorie problemów";
        public const string CategoriesUnavailable = "Nie udało się teraz wczytać kategorii. Możesz przejść do pełnej listy programistów.";
        public const string AllPractitioners = "Wszyscy programiści";
        public const string StepsTitle = "Jak to działa";
        public const string Step1Title = "Znajdź", Step1Body = "Wybierz problem i technologię, a potem porównaj zweryfikowanych programistów.";
        public const string Step2Title = "Zarezerwuj", Step2Body = "Wybierz wolny termin, opisz problem i zapłać z góry. Pieniądze trafiają do programisty dopiero po konsultacji.";
        public const string Step3Title = "Porozmawiaj", Step3Body = "Spotkacie się online w umówionym terminie. Po konsultacji możesz wystawić opinię.";
        public const string RulesTitle = "Uczciwe zasady";
        public const string RulesBody = "Kolejność na listach zależy tylko od opinii, rzetelności, dostępności i kompletności profilu. Nie da się jej kupić.";
    }

    public static class ListPage
    {
        public const string AllTitle = "Programiści";
        public const string AllIntro = "Zweryfikowani programiści, z którymi umówisz płatną konsultację online. Wybierz problem, technologię i czas trwania.";
        public const string CategoryIntro = "Konsultacje „{0}” ze zweryfikowanymi programistami. Wybierz technologię, czas i budżet, a potem zarezerwuj termin.";
        public const string CategoryTechIntro = "Konsultacje „{0}” ze zweryfikowanymi programistami. Przeglądasz wyniki zawężone do wybranej technologii.";
        public const string Count = "Wyniki: {0}", ClearFilters = "Wyczyść filtry", ResultsLabel = "Lista programistów";
        /// <summary>The count line of the result header: <c>6 programistów</c>, <c>1 programista</c>. Numerals 2 to 4 take the genitive plural too: the noun is masculine personal.</summary>
        public const string CountPhrase = "{0} {1}", PractitionerOne = "programista", PractitionerMany = "programistów";
        public const string EmptyTitle = "Nie ma programistów dla tych filtrów", EmptyBody = "Zmień albo usuń filtry, żeby zobaczyć programistów z innymi kryteriami.";
        public const string ActiveFilters = "Aktywne filtry", RemoveFilter = "usuń filtr", SearchChip = "Szukaj: „{0}”";
        public const string Unavailable = "Nie udało się teraz wczytać listy programistów. Spróbuj ponownie za chwilę.";
        public const string MetaAllTitle = "Programiści do konsultacji online | Gabinety Tech";
        public const string MetaAllDescription = "Przeglądaj zweryfikowanych programistów i umów płatną konsultację online: audyt kodu, migracja, integracja AI i więcej.";
        public const string MetaProblemTitle = "{0}: konsultacje z programistą | Gabinety Tech";
        public const string MetaProblemDescription = "{0}: umów płatną konsultację ze zweryfikowanym programistą. Dostępnych programistów: {1}.";
        public const string MetaTechTitle = "{0} i {1}: konsultacje z programistą | Gabinety Tech";
        public const string MetaTechDescription = "{0} w technologii {1}: umów płatną konsultację ze zweryfikowanym programistą. Dostępnych programistów: {2}.";
    }

    public static class Filters
    {
        public const string Label = "Filtry", Problem = "Problem", Tech = "Technologia", Duration = "Czas trwania", Price = "Cena do", Any = "Wszystkie", AnyPrice = "Dowolna";
        public const string Minutes = "{0} min", UpTo = "do {0}", SearchLabel = "Szukaj po imieniu lub nagłówku", SearchPlaceholder = "np. migracja", Search = "Szukaj";
    }

    public static class Card
    {
        public const string From = "od", IsNew = "Nowy", Rating = "Ocena {0} ({1})";
        public const string BadgeEmail = "E-mail zweryfikowany", BadgeGithub = "GitHub połączony", BadgeNip = "NIP zweryfikowany", BadgeApproved = "Zatwierdzony";
    }

    public static class Login
    {
        public const string Title = "Logowanie", MetaTitle = "Logowanie | Gabinety Tech", Email = "Adres e-mail", Password = "Hasło", Submit = "Zaloguj się";
        public const string Required = "Podaj adres e-mail i hasło.";
        public const string InvalidCredentials = "Nieprawidłowy adres e-mail lub hasło.";
        public const string VerificationRequired = "Potwierdź adres e-mail linkiem z wiadomości, a potem zaloguj się.";
        public const string TwoFactor = "Konta z uwierzytelnianiem dwuskładnikowym nie są jeszcze obsługiwane.";
        public const string Unavailable = "Logowanie jest chwilowo niedostępne. Spróbuj ponownie za chwilę.";
        public const string GithubFailed = "Logowanie przez GitHub nie powiodło się. Spróbuj ponownie lub użyj hasła.";
        public const string RateLimited = "Zbyt wiele prób logowania. Spróbuj ponownie za minutę.";
        public const string LockedOut = "Konto jest chwilowo zablokowane po wielu nieudanych próbach logowania. Spróbuj ponownie za kilka minut.";
        public const string Github = "Zaloguj się przez GitHub", Or = "albo", NoAccount = "Nie masz konta?", Summary = "Popraw dane logowania";
        public const string EmailRequired = "Podaj adres e-mail.", PasswordRequired = "Podaj hasło.", EmailInvalid = "Podaj poprawny adres e-mail.";
        public const string SessionExpired = "Sesja wygasła. Zaloguj się ponownie.";
        public const string BookingContext = "Zaloguj się, aby dokończyć rezerwację.", ShowPassword = "Pokaż hasło";
    }

    /// <summary>The second step of a two-factor sign-in (<c>/logowanie/kod</c>). The challenge itself is a sealed cookie; nothing of it is shown.</summary>
    public static class TwoFactor
    {
        public const string Title = "Kod weryfikacyjny", MetaTitle = "Kod weryfikacyjny | Gabinety Tech";
        public const string Lead = "Twoje konto ma włączone uwierzytelnianie dwuskładnikowe. Wpisz kod z aplikacji uwierzytelniającej.";
        public const string RecoveryLead = "Wpisz jeden z kodów odzyskiwania zapisanych przy włączaniu uwierzytelniania dwuskładnikowego. Każdego kodu można użyć tylko raz.";
        public const string CodeLabel = "Kod z aplikacji", RecoveryLabel = "Kod odzyskiwania", CodeHint = "6 cyfr.", Submit = "Zaloguj się";
        public const string UseRecovery = "Użyj kodu odzyskiwania", UseApp = "Użyj kodu z aplikacji", Back = "Wróć do logowania";
        public const string CodeRequired = "Wpisz kod.", Invalid = "Nieprawidłowy kod. Sprawdź go i spróbuj ponownie.";
        public const string Expired = "Czas na wpisanie kodu minął. Zaloguj się ponownie.";
        public const string RateLimited = "Zbyt wiele prób. Spróbuj ponownie za minutę.";
        public const string Unavailable = "Logowanie jest chwilowo niedostępne. Spróbuj ponownie za chwilę.";
    }

    public static class Register
    {
        public const string Title = "Załóż konto", MetaTitle = "Załóż konto | Gabinety Tech", Email = "Adres e-mail", Password = "Hasło", Submit = "Załóż konto";
        public const string BookingContext = "Załóż konto, aby dokończyć rezerwację.", ShowPassword = "Pokaż hasło";
        public const string RuleLength = "co najmniej 8 znaków", RuleCase = "mała i wielka litera", RuleDigit = "cyfra", RuleSymbol = "znak specjalny";
        public const string ConsentPrefix = "Akceptuję ", ConsentAnd = " i ", ConsentSuffix = ".", Terms = "Regulamin", Privacy = "Politykę prywatności";
        public const string VersionTitle = "Wersja z dnia {0}";
        public const string EmailRequired = "Podaj adres e-mail.", EmailInvalid = "Podaj poprawny adres e-mail.", EmailTooLong = "Adres e-mail może mieć najwyżej 254 znaki.";
        public const string PasswordRequired = "Podaj hasło.";
        public const string PasswordTooShort = "Hasło musi mieć co najmniej 8 znaków.";
        public const string PasswordWeak = "Hasło musi zawierać małą i wielką literę, cyfrę i znak specjalny.";
        public const string PasswordTooShortAndWeak = "Hasło musi mieć co najmniej 8 znaków oraz zawierać małą i wielką literę, cyfrę i znak specjalny.";
        public const string PasswordTooLong = "Hasło może mieć najwyżej 200 znaków.";
        public const string ConsentRequired = "Aby założyć konto, zaakceptuj Regulamin i Politykę prywatności.";
        public const string RateLimited = "Zbyt wiele prób. Spróbuj ponownie za minutę.";
        public const string Failed = "Nie udało się założyć konta. Sprawdź dane i spróbuj ponownie.";
        public const string Unavailable = "Rejestracja jest chwilowo niedostępna. Spróbuj ponownie za chwilę.";
        public const string HaveAccount = "Masz już konto?", SignInLink = "Zaloguj się";
        public const string TwoFactor = "Konto wymaga kodu weryfikacyjnego.";
    }

    /// <summary>The state after a registration the identity service accepted without a session (202): the address must be confirmed first. A real page, so a reload does not post again.</summary>
    public static class CheckInbox
    {
        public const string Title = "Sprawdź skrzynkę", MetaTitle = "Sprawdź skrzynkę | Gabinety Tech";
        public const string Body = "Wysłaliśmy wiadomość z linkiem potwierdzającym. Po potwierdzeniu adresu możesz się zalogować.";
        public const string Hint = "Nie widzisz wiadomości? Zajrzyj do folderu ze spamem albo spróbuj ponownie za kilka minut.";
        public const string SignIn = "Przejdź do logowania";
    }

    /// <summary>"Popraw N pól" with the Polish plural: 1 pole, 2-4 pola, 5-21 pól, 22-24 pola.</summary>
    public static class FormErrors
    {
        public const string Error = "Błąd: ";
        public static string Summary(int count) => $"Popraw {count} {(count == 1 ? "pole" : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "pola" : "pól")}";
    }

    public static class Logout
    {
        public const string Title = "Wylogowanie", MetaTitle = "Wylogowanie | Gabinety Tech", Body = "Czy chcesz się wylogować?", Submit = "Wyloguj się";
    }

    public static class Bookings
    {
        public const string Title = "Moje rezerwacje", MetaTitle = "Moje rezerwacje | Gabinety Tech";
        public const string Upcoming = "Nadchodzące", Past = "Minione", Today = "dziś", Tomorrow = "jutro", InDays = "za {0} dni", StatusFilterLabel = "Filtruj według statusu";
        public const string RoleLabel = "Pokaż rezerwacje jako", Client = "Klient", Practitioner = "Programista";
        public const string StatusLabel = "Status", AllStatuses = "Wszystkie", Loading = "Ładowanie rezerwacji…", Empty = "Brak rezerwacji w tym widoku.", More = "Pokaż więcej";
        public const string PartyPractitioner = "Programista", PartyClient = "Klient";
        public const string SignedOut = "Sesja wygasła. Zaloguj się ponownie.", SignInAgain = "Zaloguj się";
        public const string ErrorNetwork = "Brak połączenia z usługą rezerwacji. Spróbuj ponownie za chwilę.", ErrorUnavailable = "Rezerwacje są chwilowo niedostępne. Spróbuj ponownie za chwilę.";
        public const string ErrorRateLimited = "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.", ErrorForbidden = "Nie masz dostępu do tych rezerwacji.", ErrorGeneric = "Nie udało się wczytać rezerwacji.";
        public const string UnknownStatus = "Status nieznany";

        /// <summary>The Polish label of a booking status (CONTRACTS 8.1); an unknown value is shown as such, never as the raw wire value.</summary>
        public static readonly IReadOnlyDictionary<string, string> Statuses = new Dictionary<string, string>
        {
            ["PendingPayment"] = "Oczekuje na płatność",
            ["Confirmed"] = "Potwierdzona",
            ["CancelledByClient"] = "Anulowana przez klienta",
            ["LateCancelledByClient"] = "Anulowana przez klienta (bez zwrotu)",
            ["CancelledByPractitioner"] = "Anulowana przez programistę",
            ["Expired"] = "Wygasła",
            ["Completed"] = "Zakończona",
            ["ClientNoShow"] = "Klient nie stawił się",
            ["Disputed"] = "Spór w toku",
            ["Resolved"] = "Spór rozstrzygnięty",
        };
    }

    public static class Format
    {
        public const string Currency = "zł";
        /// <summary>Months in the genitive, for "28 marca".</summary>
        public static readonly string[] MonthsGenitive = ["stycznia", "lutego", "marca", "kwietnia", "maja", "czerwca", "lipca", "sierpnia", "września", "października", "listopada", "grudnia"];
        public static readonly string[] Weekdays = ["niedziela", "poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota"];
    }

    /// <summary>The account page (<c>/konto</c>, port of <c>web/src/components/auth/strings.ts</c> "account"); the versions of the documents go to a title, never to the text (DESIGN-CONTRACT 8).</summary>
    public static class Account
    {
        public const string Title = "Konto", MetaTitle = "Konto | Gabinety Tech", Email = "Adres e-mail";
        public const string Verified = "Adres e-mail potwierdzony", NotVerified = "Adres e-mail niepotwierdzony", NotVerifiedHint = "Sprawdź skrzynkę i kliknij link z wiadomości.";
        public const string Consents = "Zgody";
        public const string TermsAccepted = "Regulamin: zaakceptowany", TermsPending = "Regulamin: wymaga ponownej akceptacji";
        public const string PrivacyAccepted = "Polityka prywatności: zaakceptowana", PrivacyPending = "Polityka prywatności: wymaga ponownej akceptacji";
        public const string TermsPendingExplanation = "Zaktualizowaliśmy regulamin. Zaakceptuj nową wersję, aby potwierdzić zgodę na aktualne warunki.";
        public const string PrivacyPendingExplanation = "Zaktualizowaliśmy politykę prywatności. Zaakceptuj nową wersję, aby potwierdzić zgodę na aktualne zasady przetwarzania danych.";
        public const string VersionTitle = "Wersja dokumentu: {0}";
        public const string AcceptCurrent = "Zaakceptuj aktualne wersje";
        public const string Data = "Twoje dane", Export = "Pobierz moje dane (JSON)", SignOut = "Wyloguj się";
        public const string DeleteTitle = "Usunięcie konta";
        public const string DeleteBody = "Konto zostanie usunięte, a dane po okresie przechowywania zanonimizowane. Tej operacji nie da się cofnąć.";
        public const string DeleteConfirm = "Rozumiem i chcę usunąć konto", DeletePassword = "Hasło (potwierdzenie)", DeleteSubmit = "Usuń konto", DeleteOpen = "Usuń konto";
        public const string DeleteFailed = "Nie udało się usunąć konta. Sprawdź hasło i spróbuj ponownie.";
        public const string DeleteConfirmRequired = "Zaznacz potwierdzenie, aby usunąć konto.";
        public const string DeletePasswordRequired = "Podaj hasło, aby potwierdzić usunięcie konta.";
        public const string DeleteUnavailable = "Usuwanie konta jest chwilowo niedostępne. Spróbuj ponownie za chwilę.";
        public const string AcceptFailed = "Nie udało się zapisać zgód. Spróbuj ponownie za chwilę.";
        public const string Unavailable = "Dane konta są chwilowo niedostępne. Spróbuj ponownie za chwilę.";
    }
}
