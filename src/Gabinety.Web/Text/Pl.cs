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
    }

    public static class Nav
    {
        public const string Label = "Menu główne", Search = "Programiści", SignIn = "Zaloguj się", MyBookings = "Moje rezerwacje", SignOut = "Wyloguj się";
    }

    public static class A11y
    {
        public const string SkipToContent = "Przejdź do treści", Flash = "Komunikaty", Pagination = "Paginacja", PreviousPage = "Poprzednia strona", NextPage = "Następna strona";
        /// <summary>Read before a field error by a screen reader (the error text itself is not colour only).</summary>
        public const string ErrorPrefix = "Błąd:";
    }

    public static class Errors
    {
        public const string GenericTitle = "Coś poszło nie tak", GenericBody = "Wystąpił nieoczekiwany błąd. Spróbuj ponownie za chwilę.";
        public const string NotFoundTitle = "Nie znaleziono strony", NotFoundBody = "Strona, której szukasz, nie istnieje lub została przeniesiona.";
        public const string Reconnecting = "Łączenie z serwerem…", ReconnectFailed = "Utracono połączenie z serwerem. Odśwież stronę.";
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
        public const string Count = "Wyniki: {0}", Empty = "Brak programistów dla wybranych filtrów.", ClearFilters = "Wyczyść filtry", ResultsLabel = "Lista programistów";
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
    }

    public static class Logout
    {
        public const string Title = "Wylogowanie", MetaTitle = "Wylogowanie | Gabinety Tech", Body = "Czy chcesz się wylogować?", Submit = "Wyloguj się";
    }

    public static class Bookings
    {
        public const string Title = "Moje rezerwacje", MetaTitle = "Moje rezerwacje | Gabinety Tech";
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
}
