namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of the practitioner's area (<c>/gabinet-programisty/*</c>). Strings of the Next.js area (<c>web/src/components/practitioner/strings.ts</c>) are verbatim where the journeys read them
/// (J2: the checklist item names, <c>Gotowe</c>, <c>Do zrobienia</c>, <c>Szkic</c>, <c>Zapisano.</c>, the submit and GitHub messages); the rest follows docs/ui/DESIGN-CONTRACT.md section 8.
/// </summary>
public static class PlPractitioner
{
    public const string Title = "Panel programisty", SectionsLabel = "Sekcje panelu programisty";

    public static class Tabs
    {
        public const string Overview = "Start", Profile = "Profil", Offers = "Oferty", Availability = "Dostępność", Payments = "Płatności";
    }

    public static class Common
    {
        public const string Unavailable = "Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Retry = "Spróbuj ponownie";
        public const string Save = "Zapisz", Saving = "Zapisywanie…", Optional = "opcjonalnie", Counter = "{0} / {1}", Remove = "Usuń";
        public const string Network = "Nie udało się połączyć z serwerem. Spróbuj ponownie.", Forbidden = "Nie masz dostępu do tej części panelu.";
        public const string ErrorsSummary = "Popraw {0} {1}", FieldOne = "pole", FieldFew = "pola", FieldMany = "pól", ErrorWord = "Błąd:";
        public const string SignedOut = "Sesja wygasła. Zaloguj się ponownie.", SignInAgain = "Zaloguj się";
        public const string StatusOf = "Status profilu";
    }

    /// <summary>The flash messages of this area, by key (see <c>FlashKey</c>).</summary>
    public static class Flash
    {
        public const string Saved = "Zapisano.", Submitted = "Profil wysłany. Administrator sprawdzi go i da znać.", Created = "Gabinet został założony. Uzupełnij kroki poniżej.";
        public const string OfferDisabled = "Oferta została wyłączona. Możesz ją włączyć z powrotem na liście.", OfferEnabled = "Oferta została włączona.";
    }

    public static class Create
    {
        public const string Title = "Załóż swój gabinet", MetaTitle = "Załóż gabinet | Gabinety Tech";
        public const string Intro = "Zacznij od nazwy, pod którą klienci zobaczą Twój profil. Resztę uzupełnisz w kolejnych krokach, a przed publikacją sprawdzi ją administrator.";
        public const string OpenToAll = "Każde zalogowane konto może założyć gabinet programisty. Nie musisz zakładać osobnego konta, a konto klienta zostaje bez zmian.";
        public const string StepsTitle = "Jak to wygląda";
        public static readonly string[] Steps =
        [
            "Profil: nagłówek, opis, link do portfolio i NIP.",
            "Oferta: co najmniej jedna aktywna konsultacja z ceną.",
            "GitHub: połączenie konta jednorazowym kodem.",
            "Weryfikacja: administrator sprawdza profil, zwykle w ciągu kilku dni roboczych.",
        ];
        public const string DisplayName = "Nazwa wyświetlana", DisplayNameHint = "2 do 80 znaków. Pod tą nazwą klienci zobaczą Twój profil.", Submit = "Załóż gabinet";
        public const string Exists = "Masz już gabinet. Odśwież stronę.";
        public const string Failed = "Nie udało się założyć gabinetu. Sprawdź nazwę (2 do 80 znaków) i spróbuj ponownie.";
    }

    public static class Status
    {
        public const string Draft = "Szkic", Submitted = "Czeka na weryfikację", Approved = "Zatwierdzony", Rejected = "Odrzucony", Suspended = "Zawieszony", Unknown = "Status nieznany";
        public const string Reason = "Powód: {0}", RejectedHelp = "Popraw profil zgodnie z powodem i wyślij go ponownie.";
        public const string SubmittedHelp = "Administrator sprawdza Twój profil. Dostaniesz znać, gdy decyzja zapadnie.";
        public const string ApprovedHelp = "Twój profil jest publiczny. Klienci mogą rezerwować terminy, gdy masz aktywną ofertę i godziny dostępności.";
        public const string SuspendedHelp = "Twój profil jest zawieszony i nie jest widoczny dla klientów.";
    }

    public static class Dashboard
    {
        public const string MetaTitle = "Panel programisty | Gabinety Tech";
        public const string ChecklistTitle = "Lista kroków przed wysłaniem do weryfikacji", Done = "Gotowe", Todo = "Do zrobienia", Fill = "Uzupełnij";
        public const string ProgressLabel = "Postęp: {0} z {1}";
        public const string Submit = "Wyślij do weryfikacji", Submitting = "Wysyłanie…", SubmitWhatHappens = "Po wysłaniu administrator sprawdzi profil. Do czasu decyzji nie możesz go wysłać ponownie.";
        public const string SubmitBlocked = "Uzupełnij jeszcze {0} {1}, aby wysłać profil.", StepOne = "krok", StepFew = "kroki", StepMany = "kroków";
        public const string Incomplete = "Profil nie jest jeszcze kompletny. Brakuje: {0}.", SubmitFailed = "Nie udało się wysłać profilu. Spróbuj ponownie.";
        public const string InvalidTransition = "Profil w obecnym statusie nie może być wysłany.";
        public const string NextTitle = "Co dalej", NextLabel = "Następny krok", Ready = "Gotowe", Pending = "Do zrobienia";
        public const string StepVerification = "Weryfikacja profilu", StepOffers = "Oferty", StepAvailability = "Dostępność", StepPayments = "Płatności";
        public const string StepVerificationDone = "Profil jest zatwierdzony.", StepVerificationTodo = "Uzupełnij profil i wyślij go do weryfikacji.", StepVerificationWaiting = "Administrator sprawdza profil.";
        public const string StepOffersDone = "Masz co najmniej jedną aktywną ofertę.", StepOffersTodo = "Dodaj ofertę, którą klienci będą rezerwować.";
        public const string StepAvailabilityDone = "Godziny przyjęć są ustawione.", StepAvailabilityTodo = "Ustaw tygodniowe godziny, w których przyjmujesz klientów.";
        public const string StepPaymentsDone = "Konto płatności jest gotowe.", StepPaymentsTodo = "Skonfiguruj konto płatności, aby otrzymywać wypłaty.", StepPaymentsDemo = "Tryb demonstracyjny: nic nie trzeba konfigurować.";
        public const string StepPaymentsUnknown = "Nie udało się sprawdzić konta płatności.";
        public const string GoTo = "Przejdź";
        public const string PaymentsBannerTitle = "Płatności nie są jeszcze gotowe";
        public const string PaymentsBannerBody = "Bez gotowego konta płatności nie możesz przyjmować rezerwacji: klienci nie zapłacą, a Ty nie dostaniesz wypłaty.";
        public const string PaymentsBannerLink = "Skonfiguruj płatności";
        public const string ProgressAria = "Postęp uzupełniania profilu";
        public const string PublicTitle = "Twój profil jest publiczny", PublicBody = "Klienci mogą go znaleźć i zarezerwować konsultację.", ViewPublic = "Zobacz profil publiczny", CopyLink = "Skopiuj link", Copied = "Skopiowano";
        public const string UpcomingTitle = "Nadchodzące konsultacje", UpcomingEmpty = "Nie masz jeszcze zaplanowanych konsultacji.", UpcomingFailed = "Nie możemy teraz pokazać konsultacji.";
        public const string Details = "Szczegóły", AllBookings = "Wszystkie rezerwacje";
    }

    public static class Checklist
    {
        public const string Headline = "Nagłówek profilu", Bio = "Opis (co najmniej 200 znaków)", Offering = "Co najmniej jedna aktywna oferta", Github = "Połączone konto GitHub";
        public const string Nip = "Poprawny numer NIP", Portfolio = "Link do portfolio", Email = "Potwierdzony adres e-mail";
        public const string HelpProfile = "Uzupełnij w zakładce Profil.", HelpOffers = "Dodaj ofertę w zakładce Oferty.", HelpGithub = "Połącz konto poniżej.";
        public const string HelpNip = "Wpisz NIP w zakładce Profil.", HelpPortfolio = "Dodaj link w zakładce Profil.", HelpEmail = "Kliknij link z wiadomości, którą wysłaliśmy przy rejestracji.";
    }

    public static class Github
    {
        public const string Title = "Konto GitHub";
        public const string Help = "Wpisz swój login na GitHubie. Pokażemy jednorazowy kod, który wklejasz do publicznego opisu (bio) tego profilu na GitHubie, a potem sprawdzamy go tutaj.";
        public const string Login = "Login GitHub", Request = "Pokaż kod", CodeIntro = "Wklej ten kod do opisu profilu na GitHubie (ważny 30 minut):", Verify = "Sprawdź kod";
        public const string Linked = "Połączono konto GitHub: {0}.";
        public const string NotLinked = "Nie znaleźliśmy kodu w opisie profilu GitHub. Sprawdź, czy go wkleiłeś, i spróbuj ponownie.";
        public const string InvalidLogin = "Login GitHub to 1 do 39 znaków: litery, cyfry i myślniki.", Unavailable = "GitHub nie odpowiada. Spróbuj ponownie za chwilę.";
        public const string Failed = "Nie udało się połączyć konta GitHub.", AlreadyLinked = "To konto GitHub jest już połączone z innym profilem.";
    }

    public static class Profile
    {
        public const string Title = "Profil", MetaTitle = "Profil | Gabinety Tech", DisplayName = "Nazwa wyświetlana", Headline = "Nagłówek";
        public const string HeadlineHint = "10 do 120 znaków, bez adresów e-mail, numerów telefonu i komunikatorów.";
        // The label keeps the word "Opis" (journey J2 finds it with getByLabel("Opis")); the audit's "O mnie" (G-10) is in the brackets.
        public const string Bio = "Opis (o mnie)", BioHint = "Do 2000 znaków, zwykły tekst, bez danych kontaktowych. Do wysyłki potrzeba co najmniej 200 znaków.";
        public const string Portfolio = "Link do portfolio", PortfolioHint = "Adres https, na przykład strona z Twoimi projektami.";
        public const string Meeting = "Domyślny link do spotkań", MeetingHint = "Adres https z Google Meet, Zoom, Teams, Whereby lub Jitsi. Nigdy nie jest publiczny.";
        public const string Nip = "NIP", NipHint = "10 cyfr. Sprawdzamy sumę kontrolną. Zmiana NIP zatwierdzonego profilu wymaga ponownej weryfikacji.";
        public const string NipChangeWarning = "Zmiana NIP wyśle zatwierdzony profil ponownie do weryfikacji.";
        public const string Slug = "Adres profilu";
        /// <summary>The caption of the preview of the public header beside the form (UX-043); it shows the saved values, not the typed ones.</summary>
        public const string PreviewTitle = "Podgląd profilu";
    }

    public static class Offers
    {
        public const string Title = "Oferty", MetaTitle = "Oferty | Gabinety Tech", Empty = "Nie masz jeszcze żadnej oferty. Dodaj pierwszą poniżej.";
        public const string ListLabel = "Twoje oferty", Edit = "Edytuj", Disable = "Wyłącz", Enable = "Włącz", Active = "Aktywna", Inactive = "Wyłączona", ActiveCount = "Aktywne oferty: {0} z 5";
        public const string FormTitleNew = "Nowa oferta", FormTitleEdit = "Edycja oferty", Name = "Nazwa oferty", NameHint = "5 do 80 znaków.";
        public const string Description = "Opis oferty", DescriptionHint = "Do 600 znaków, bez danych kontaktowych.";
        public const string Duration = "Czas trwania", D30 = "30 minut", D60 = "60 minut", Price = "Cena (zł)", PriceHint = "Od 50 do 1500 zł, na przykład 250 lub 250,50.";
        public const string Problem = "Rodzaj problemu", Choose = "Wybierz…", Techs = "Technologie (do 4)", TechsHint = "Wybierz do 4 technologii.";
        public const string Meeting = "Link do spotkania tej oferty", MeetingHint = "Zostaw puste, aby użyć domyślnego linku z profilu.";
        public const string Cancel = "Anuluj", TooMany = "Możesz mieć najwyżej 5 aktywnych ofert. Wyłącz jedną, aby dodać następną.";
        public const string PriceChangeNote = "Zmiana ceny lub czasu dotyczy tylko rezerwacji dokonanych po zapisie.";
        public const string TaxonomyUnavailable = "Nie udało się pobrać listy kategorii. Odśwież stronę.", Failed = "Nie udało się zapisać oferty. Spróbuj ponownie.";
        public const string PriceFormat = "Wpisz cenę w złotych, na przykład 250 lub 250,50 (najwyżej dwa miejsca po przecinku).", PriceRange = "Cena musi mieścić się między 50 a 1500 zł.";
        public const string ConfirmTitle = "Wyłączyć ofertę „{0}”?", ConfirmBody = "Klienci nie zobaczą jej na profilu i nie zarezerwują nowych terminów. Istniejące rezerwacje zostają. Możesz ją włączyć z powrotem.";
        public const string ConfirmYes = "Tak, wyłącz ofertę", ConfirmNo = "Nie, zostaw aktywną", NotFound = "Nie znaleziono tej oferty.";
        public const string AddNew = "Dodaj ofertę", TechCount = "Wybrano {0} z 4", Minutes = "{0} min";
        public const string PreviewLabel = "Podgląd oferty", PreviewNote = "Tak zobaczą ją klienci na Twoim profilu. Podgląd pokazuje wartości zapisane w formularzu.";
        public const string PreviewTitle = "Tytuł oferty", PreviewDescription = "Opis pojawi się tutaj.", PreviewPrice = "cena w zł";
    }

    public static class Availability
    {
        public const string Title = "Dostępność", MetaTitle = "Dostępność | Gabinety Tech";
        public const string Intro = "Ustal tygodniowe godziny, w których przyjmujesz klientów (czas polski, Europe/Warsaw), oraz dni wolne. Godziny zaczynają się i kończą o pełnej lub wpół do pełnej.";
        public const string Weekly = "Godziny tygodniowe", NoRules = "Brak godzin: klienci nie zobaczą żadnych terminów.", AddRule = "Dodaj godziny";
        public const string Day = "Dzień tygodnia", From = "Od", To = "Do", Row = "Przedział {0}";
        public static readonly string[] Days = ["poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota", "niedziela"];
        public const string Exceptions = "Dni wolne", ExceptionDate = "Data dnia wolnego", AddException = "Dodaj dzień wolny", NoExceptions = "Brak dni wolnych.";
        public const string TooManyRules = "Możesz mieć najwyżej 28 przedziałów godzin.", TooManyExceptions = "Możesz mieć najwyżej 90 dni wolnych.";
        public const string NotHalfHour = "Godziny muszą być pełne lub wpół do pełnej, najpóźniej 23:30.", EndBeforeStart = "Koniec musi być po początku.";
        public const string Overlap = "Przedziały w dniu „{0}” nakładają się. Popraw je przed zapisem.", DuplicateException = "Ten sam dzień wolny jest wpisany dwa razy.";
        public const string BadWeekday = "Nieprawidłowy dzień tygodnia.", BadDate = "Wpisz prawdziwą datę dnia wolnego.", RowIssue = "Przedział {0}: {1}";
        public const string ServerRow = "sprawdź dzień i godziny", Failed = "Nie udało się zapisać dostępności. Spróbuj ponownie.", SummaryTitle = "Popraw godziny przed zapisem";
        public const string Unsaved = "Zmiany w liście godzin są zapisane dopiero po kliknięciu „Zapisz”.";
        public const string CopyToWeekdays = "Skopiuj na pozostałe dni robocze", DayEmpty = "Brak godzin tego dnia.";
    }

    public static class Payments
    {
        public const string Retry = "Spróbuj ponownie", Title = "Płatności", MetaTitle = "Płatności | Gabinety Tech";
        public const string Intro = "Klienci płacą z góry, a pieniądze trafiają do Ciebie po konsultacji, na konto płatności, które zakładasz u naszego operatora płatności (Stripe). Konfiguracja trwa kilka minut.";
        public const string StatusLabel = "Status konta płatności", Ready = "Gotowe", NeedsWork = "Wymaga uzupełnienia", NotStarted = "Nie rozpoczęto";
        public const string MustBeReady = "Bez gotowego konta płatności nie możesz przyjmować rezerwacji: klienci nie zapłacą, a Ty nie dostaniesz wypłaty.";
        public const string ReadyBody = "Konto płatności jest gotowe. Klienci mogą rezerwować Twoje terminy, a wypłaty trafiają na Twoje konto po konsultacji.";
        public const string PendingBody = "Konto płatności czeka na uzupełnienie danych u operatora płatności. Dokończ konfigurację, aby przyjmować rezerwacje.";
        public const string NoneBody = "Nie rozpocząłeś jeszcze konfiguracji konta płatności.";
        /// <summary>What a client meets while the account is not ready: the booking is refused (Booking answers 422 without a ready account), so no payment is taken.</summary>
        public const string ClientMeanwhile = "Co widzi klient w międzyczasie: nie może zarezerwować u Ciebie terminu ani zapłacić, dopóki konto płatności nie jest gotowe.";
        public const string Start = "Rozpocznij konfigurację płatności", Continue = "Dokończ konfigurację płatności";
        public const string LinkReady = "Link do konfiguracji jest gotowy. Jest jednorazowy i krótko ważny.", OpenLink = "Przejdź do konfiguracji w Stripe";
        public const string Returned = "Wróciłeś z konfiguracji. Poniżej jest aktualny status konta płatności.";
        public const string Refresh = "Link do konfiguracji wygasł albo został już użyty. Wygeneruj nowy przyciskiem poniżej.";
        public const string Demo = "Tryb demonstracyjny: płatności są symulowane, więc nie musisz niczego konfigurować.";
        public const string Unavailable = "Nie udało się teraz sprawdzić konta płatności. Spróbuj ponownie za chwilę.", StartFailed = "Nie udało się przygotować konfiguracji płatności. Spróbuj ponownie za chwilę.";
        public const string NotPractitioner = "Konto płatności możesz założyć po założeniu gabinetu.", CreateFirst = "Załóż gabinet";
    }

    /// <summary>What to do about a field the service complained about, by the field's name (CONTRACTS 3.5); the service's own messages are never shown.</summary>
    public static class Errors
    {
        public const string ContactDetails = "Usuń dane kontaktowe (adres e-mail, numer telefonu, komunikator). Dane kontaktowe wymieniasz dopiero po rezerwacji.", Unknown = "To pole jest nieprawidłowe.";
        public static readonly IReadOnlyDictionary<string, string> ByField = new Dictionary<string, string>
        {
        ["displayName"] = "Nazwa ma mieć od 2 do 80 znaków, bez znaków sterujących.",
        ["headline"] = "Nagłówek ma mieć od 10 do 120 znaków.",
        ["bio"] = "Opis może mieć najwyżej 2000 znaków.",
        ["portfolioUrl"] = "Podaj adres https na prawdziwej domenie, do 300 znaków.",
        ["meetingUrlDefault"] = "Podaj adres https z dozwolonego serwisu spotkań, do 300 znaków.",
        ["meetingUrl"] = "Podaj adres https z dozwolonego serwisu spotkań, do 300 znaków.",
        ["nip"] = "NIP ma mieć 10 cyfr i poprawną sumę kontrolną.",
        ["title"] = "Nazwa oferty ma mieć od 5 do 80 znaków, bez znaków sterujących.",
        ["description"] = "Opis oferty może mieć najwyżej 600 znaków.",
        ["durationMinutes"] = "Wybierz 30 lub 60 minut.",
        ["priceMinor"] = Offers.PriceRange,
        ["problemSlug"] = "Wybierz rodzaj problemu z listy.",
        ["techSlugs"] = "Wybierz najwyżej 4 różne technologie z listy.",
        };
    }
}
