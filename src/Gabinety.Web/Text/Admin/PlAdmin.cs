namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of the administrator area (SPEC 5 rule 1), one static class per page family. Strings the journeys read (J2 verification, J6 dispute resolution) and the strings of the
/// Next.js admin pages are copied verbatim; what the UI audit (P-ADM-1..6) asked to change is new text, marked below.
/// </summary>
public static class PlAdmin
{
    public static class Shell
    {
        public const string Strip = "Administracja", NavLabel = "Sekcje administracji", Title = "Administracja", MetaTitle = "Administracja | Gabinety Tech";
        public const string Lead = "Wybierz sekcję. Wszystkie zmiany zapisują się w historii i wymagają potwierdzenia tam, gdzie nie da się ich cofnąć.";
        public const string Verification = "Weryfikacja programistów", Disputes = "Spory", Ledger = "Księga", Reviews = "Opinie", Suspicious = "Podejrzane pary";
        public const string VerificationLead = "Zgłoszone profile czekają na decyzję: zatwierdź albo odrzuć z powodem.";
        public const string DisputesLead = "Zgłoszenia klientów po konsultacji: wypłata, zwrot częściowy albo pełny zwrot.";
        public const string LedgerLead = "Wpisy księgi płatności w wybranym okresie i eksport do CSV.";
        public const string ReviewsLead = "Opinie wstrzymane przez asystenta i opublikowane, które trzeba sprawdzić.";
        public const string SuspiciousLead = "Pary klient–programista, które wyglądają na wzajemne podbijanie ocen.";
        public const string Home = "Wróć na stronę główną";
    }

    /// <summary>F-10: a signed-in user who is not an administrator gets this page with status 403 (the hook <c>data-testid="forbidden"</c>).</summary>
    public static class Forbidden
    {
        public const string Title = "Brak dostępu", MetaTitle = "Brak dostępu | Gabinety Tech";
        public const string Body = "Nie masz dostępu do panelu administracyjnego. Ta część serwisu jest tylko dla administratorów.";
        public const string Home = "Wróć na stronę główną";
    }

    public static class Common
    {
        public const string Status = "Status", More = "Starsze wpisy dalej", Back = "Wróć", Cancel = "Anuluj", Retry = "Spróbuj ponownie", Unknown = "Nieznany", None = "brak";
        public const string Reference = "Numer", SignedOut = "Sesja wygasła. Zaloguj się ponownie.", SignInAgain = "Zaloguj się";
        public const string Forbidden = "Nie masz uprawnień do tej akcji.", NotFound = "Nie znaleziono wpisu. Mógł zostać usunięty.";
        public const string Network = "Nie udało się połączyć z serwerem. Spróbuj ponownie.", RateLimited = "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.";
        public const string Generic = "Nie udało się wykonać akcji. Spróbuj ponownie.";
        public const string StaleForm = "Ten wpis już się zmienił. Lista została odświeżona.";
        public const string Client = "Klient", ClientRef = "Klient {0}", PractitionerRef = "Programista {0}";
        public const string ErrorPrefix = "Błąd:";
        /// <summary>Months of a short date ("24 lis 2026, 13:00": P-ADM-3).</summary>
        public static readonly string[] MonthsShort = ["sty", "lut", "mar", "kwi", "maj", "cze", "lip", "sie", "wrz", "paź", "lis", "gru"];
    }

    public static class Flash
    {
        public const string Approved = "Programista został zatwierdzony.", Rejected = "Programista został odrzucony.", Suspended = "Programista został zawieszony.", Reinstated = "Programista został przywrócony.";
        public const string DisputeResolved = "Spór został rozstrzygnięty.";
        public const string ReviewPublished = "Opinia została opublikowana.", ReviewRejected = "Opinia została odrzucona.";
    }

    // ---- verification queue and detail (copied from web/src/components/admin/verification/strings.ts)
    public static class Verification
    {
        public const string Title = "Weryfikacja programistów", MetaTitle = "Weryfikacja programistów | Gabinety Tech";
        public const string Unavailable = "Kolejka jest chwilowo niedostępna. Spróbuj ponownie za chwilę.";
        public const string Empty = "Brak programistów w tym statusie.", Next = "Starsze wpisy dalej";
        public const string FilterLabel = "Status";
        public const string Github = "GitHub", GithubLinked = "powiązany", GithubNot = "niepowiązany", Nip = "NIP", NipValid = "poprawny", NipInvalid = "niepoprawny lub brak";
        public const string Submitted = "Zgłoszono", Created = "Utworzono", Reason = "Powód", Slug = "Adres profilu", Bio = "Opis", Portfolio = "Portfolio", Updated = "Zmieniono", Approved = "Zatwierdzono";
        public const string Approve = "Zatwierdź", Reject = "Odrzuć", Suspend = "Zawieś", Reinstate = "Przywróć", ConfirmReject = "Potwierdź odrzucenie", ConfirmSuspend = "Potwierdź zawieszenie";
        public const string ReasonLabel = "Powód (10 do 500 znaków)", ReasonHint = "Powód zobaczy w historii weryfikacji każdy administrator. Od 10 do 500 znaków.", ReasonInvalid = "Podaj powód: od 10 do 500 znaków.";
        public const string RejectWarning = "Odrzucenie zamyka zgłoszenie. Programista dostanie powód i będzie mógł poprawić profil.", SuspendWarning = "Zawieszenie ukrywa profil i anuluje przyszłe rezerwacje z pełnym zwrotem dla klientów.";
        public const string DetailTitle = "Szczegóły programisty", DetailMetaTitle = "Szczegóły programisty | Gabinety Tech", Data = "Dane programisty", BackToQueue = "Wróć do kolejki";
        public const string DetailUnavailable = "Dane programisty są chwilowo niedostępne. Spróbuj ponownie za chwilę.", DetailNotFound = "Nie znaleziono programisty.";
        public const string TimelineTitle = "Historia weryfikacji", TimelineEmpty = "Brak zdarzeń weryfikacji.", TimelineUnavailable = "Historia weryfikacji jest chwilowo niedostępna.";
        public const string Actor = "Wykonał", Note = "Notatka";
        public const string StateChanged = "Status się zmienił, odśwież stronę.", NotFound = "Nie znaleziono programisty.";
        public const string SuspendedButBookings = "Programista jest zawieszony, ale jego przyszłych rezerwacji nie udało się jeszcze wszystkich anulować. Powtórz anulowanie (RUNBOOK 9.2).";

        public static readonly IReadOnlyDictionary<string, string> Filters = new Dictionary<string, string>
        {
            ["Submitted"] = "Czeka na weryfikację", ["Approved"] = "Zatwierdzeni", ["Suspended"] = "Zawieszeni", ["Rejected"] = "Odrzuceni",
        };

        public static readonly IReadOnlyDictionary<string, string> Statuses = new Dictionary<string, string>
        {
            ["Draft"] = "Szkic", ["Submitted"] = "Czeka na weryfikację", ["Approved"] = "Zatwierdzony", ["Rejected"] = "Odrzucony", ["Suspended"] = "Zawieszony",
        };

        public static readonly IReadOnlyDictionary<string, string> Events = new Dictionary<string, string>
        {
            ["Submitted"] = "Zgłoszono do weryfikacji", ["Approved"] = "Zatwierdzono", ["Rejected"] = "Odrzucono", ["Suspended"] = "Zawieszono", ["Reinstated"] = "Przywrócono",
            ["GithubLinked"] = "Powiązano konto GitHub", ["NipChanged"] = "Zmieniono NIP",
        };
        public const string EventUnknown = "Inne zdarzenie";
    }

    // ---- disputes (copied from web/src/components/admin/disputes/strings.ts; the card facts are new: P-ADM-3)
    public static class Disputes
    {
        public const string Title = "Spory", MetaTitle = "Spory | Gabinety Tech";
        public const string Unavailable = "Lista sporów jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Next = "Więcej";
        public const string FilterLabel = "Status", Open = "Otwarte", Resolved = "Rozstrzygnięte";
        public const string EmptyOpen = "Brak otwartych sporów.", EmptyResolved = "Brak rozstrzygniętych sporów.";
        public const string Refunded = "Zwrócono: {0}";
        public const string Offer = "Konsultacja", Client = "Klient", Practitioner = "Programista", Price = "Cena rezerwacji", MaxRefund = "Najwyższy możliwy zwrot", Payment = "Płatność", Booking = "Rezerwacja";
        public const string Opened = "Otwarto", ResolvedAt = "Rozstrzygnięto", Text = "Treść zgłoszenia", Outcome = "Rozstrzygnięcie", Note = "Notatka moderatora", BookingUnreadable = "Szczegóły rezerwacji są chwilowo niedostępne.";
        public const string ResolveOpen = "Rozstrzygnij", Legend = "Rozstrzygnięcie sporu";
        public const string Release = "Wypłać programiście", RefundFull = "Zwróć klientowi całość", RefundPartial = "Zwróć klientowi część";
        public const string ReleaseHint = "Klient nie dostaje zwrotu, programista dostaje wypłatę.", RefundFullHint = "Klient dostaje całą cenę, programista nic.", RefundPartialHint = "Klient dostaje podaną kwotę, programista resztę.";
        public const string Amount = "Kwota zwrotu w zł", AmountHint = "Na przykład 12,50. Najwyżej dwa miejsca po przecinku.", AmountHintMax = "Na przykład 12,50. Od 0,01 zł do {0}.";
        public const string NoteLabel = "Notatka (10 do 500 znaków)", NoteHint = "Notatkę zobaczą tylko administratorzy.";
        public const string Review = "Przejdź do potwierdzenia", Confirm = "Potwierdź rozstrzygnięcie", Back = "Wróć", Cancel = "Anuluj";
        public const string Pick = "Wybierz rozstrzygnięcie.", NoteInvalid = "Podaj notatkę: od 10 do 500 znaków.";
        public const string AmountFormat = "Podaj kwotę, na przykład 12,50.", AmountDecimals = "Kwota może mieć najwyżej dwa miejsca po przecinku.";
        public const string AmountRange = "Kwota zwrotu musi być większa od zera i mniejsza od ceny rezerwacji.", AmountRangeWith = "Kwota zwrotu musi być większa od zera i mniejsza od ceny ({0}).";
        public const string FormSummary = "Popraw formularz:";
        public const string ConfirmTitle = "Sprawdź skutki przed zatwierdzeniem. Tej decyzji nie można cofnąć.";
        public const string StateChanged = "Stan się zmienił, odśwież stronę.", NotFound = "Nie znaleziono rezerwacji.", ValidationRefused = "Serwer odrzucił dane rozstrzygnięcia.";
        public const string PaymentUnavailable = "Operator płatności jest chwilowo niedostępny. Spróbuj ponownie za chwilę.", Failed = "Nie udało się zapisać rozstrzygnięcia. Spróbuj ponownie.";

        public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>
        {
            ["not_as_described"] = "Usługa niezgodna z opisem", ["practitioner_no_show"] = "Nieobecność programisty", ["other"] = "Inny powód",
        };
        public const string KindUnknown = "Inny powód";

        public static readonly IReadOnlyDictionary<string, string> Resolutions = new Dictionary<string, string>
        {
            ["release"] = "Wypłata dla programisty", ["refund_full"] = "Pełny zwrot dla klienta", ["refund_partial"] = "Częściowy zwrot dla klienta",
        };

        public static readonly IReadOnlyDictionary<string, string> PaymentStates = new Dictionary<string, string>
        {
            ["Created"] = "Oczekuje", ["Succeeded"] = "Opłacona", ["Failed"] = "Nieudana", ["Refunded"] = "Zwrócona w całości", ["PartiallyRefunded"] = "Zwrócona częściowo",
        };

        // What the money does, in words (CONTRACTS 9.2). {0} price, {1} refund, {2} kept. Copied from the Next.js page.
        public const string ConfirmRelease = "Klient nie dostanie zwrotu. Programista otrzyma {0} pomniejszone o prowizję platformy (zasada pierwszej konsultacji).";
        public const string ConfirmRefundFull = "Klient dostanie zwrot całości ({0}). Programista nie otrzyma nic, prowizji nie naliczamy.";
        public const string ConfirmPartial = "Klient dostanie zwrot {0}. Programista otrzyma resztę ({1}) pomniejszoną o prowizję liczoną od tej reszty.";
        public const string ConfirmReleaseNoPrice = "Klient nie dostanie zwrotu. Programista otrzyma całą cenę rezerwacji pomniejszoną o prowizję platformy (zasada pierwszej konsultacji).";
        public const string ConfirmRefundFullNoPrice = "Klient dostanie zwrot całej ceny rezerwacji. Programista nie otrzyma nic, prowizji nie naliczamy.";
        public const string ConfirmPartialNoPrice = "Klient dostanie zwrot {0}. Programista otrzyma resztę ceny rezerwacji pomniejszoną o prowizję liczoną od tej reszty.";
    }

    // ---- review moderation (copied from web/src/components/admin/reviews/strings.ts)
    public static class Reviews
    {
        public const string Title = "Opinie do moderacji", MetaTitle = "Opinie do moderacji | Gabinety Tech";
        public const string Unavailable = "Lista opinii jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Next = "Starsze wpisy dalej";
        public const string FilterLabel = "Status", Held = "Wstrzymane", Published = "Opublikowane", Rejected = "Odrzucone";
        public const string EmptyHeld = "Brak opinii oczekujących na sprawdzenie.", EmptyPublished = "Brak opublikowanych opinii.", EmptyRejected = "Brak odrzuconych opinii.";
        public const string Rating = "Ocena", RatingOf = "{0}/5", Created = "Dodano", PublishedAt = "Opublikowano", Practitioner = "Programista", Triage = "Wstępna ocena asystenta", Reasons = "Powody wstrzymania";
        public const string Text = "Treść opinii", Note = "Powód odrzucenia", Moderator = "Moderator";
        public const string TriageNone = "Bez zastrzeżeń", TriageHold = "Wstrzymana przez asystenta", TriageUnavailable = "Asystent był niedostępny, opinię opublikowano bez wstępnej oceny", TriageUnknown = "Brak informacji";
        public const string ReasonSpam = "Spam", ReasonAbuse = "Obraźliwa treść", ReasonPersonalData = "Dane osobowe", ReasonOffTopic = "Niezwiązana z tematem", ReasonThreat = "Groźby", ReasonOther = "Inny powód", ReasonNone = "Brak podanych powodów";
        public const string Publish = "Opublikuj", Reject = "Odrzuć", Label = "Powód odrzucenia (10 do 500 znaków)", Hint = "Powód zobaczą administratorzy. Od 10 do 500 znaków.", Confirm = "Potwierdź odrzucenie", Cancel = "Anuluj";
        public const string ReasonInvalid = "Podaj powód: od 10 do 500 znaków.", Warning = "Odrzucenie usuwa opinię z profilu programisty.";
        public const string StateChanged = "Stan opinii się zmienił, lista została odświeżona.", NotFound = "Nie znaleziono opinii.", ReasonRefused = "Serwer odrzucił powód: musi mieć od 10 do 500 znaków.";
        public const string ServiceUnavailable = "Usługa opinii jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Failed = "Nie udało się zapisać decyzji. Spróbuj ponownie.";

        public static readonly IReadOnlyDictionary<string, string> Statuses = new Dictionary<string, string>
        {
            ["Held"] = "Wstrzymana", ["Published"] = "Opublikowana", ["Rejected"] = "Odrzucona",
        };
    }

    // ---- ledger (copied from web/src/components/admin/ledger/strings.ts; the table is new: P-ADM-4)
    public static class Ledger
    {
        public const string Title = "Księga", MetaTitle = "Księga | Gabinety Tech";
        public const string Intro = "Wpisy księgi w wybranym okresie i eksport do pliku CSV (kolumny: rezerwacja, rodzaj, kwota w groszach, waluta, data). Daty to dni UTC, obie granice włącznie.";
        public const string FilterLabel = "Okres", From = "Od", To = "Do", Show = "Pokaż okres";
        public const string PresetLabel = "Szybki wybór", ThisMonth = "Ten miesiąc", LastMonth = "Poprzedni miesiąc", ThisYear = "Ten rok";
        public const string Download = "Pobierz CSV", Summary = "Wybrany okres: {0}.";
        public const string TotalsTitle = "Sumy po rodzajach", TotalsPartial = "Sumy po rodzajach pokazujemy, gdy okres ma nie więcej niż {0} wpisów. Pełny okres zawiera plik CSV.";
        public const string ProblemFormat = "Podaj obie daty w formacie rrrr-mm-dd.", ProblemOrder = "Data końcowa nie może być wcześniejsza niż początkowa.", ProblemRange = "Okres może mieć najwyżej 366 dni.";
        public const string Unauthorized = "Sesja wygasła. Zaloguj się ponownie.", Forbidden = "Nie masz uprawnień do pobrania księgi.", Validation = "Serwer odrzucił okres. Sprawdź daty.";
        public const string Unavailable = "Księga jest chwilowo niedostępna. Spróbuj ponownie za chwilę.";
        public const string TableLabel = "Wpisy księgi", Empty = "Brak wpisów w tym okresie.", Showing = "Pierwsze {0} wpisów okresu. Pełną listę zawiera plik CSV.", ShowingAll = "Wpisów w okresie: {0}.";
        public const string ColBooking = "Rezerwacja", ColKind = "Rodzaj", ColAmount = "Kwota", ColDate = "Data";
        public const string KindClientCharge = "Płatność klienta", KindRefund = "Zwrot dla klienta", KindPlatformFee = "Prowizja platformy", KindPractitionerPayout = "Wypłata dla programisty", KindPayoutReversal = "Cofnięcie wypłaty";
        public const string KindUnknown = "Inny wpis";
        /// <summary>The body of the download route's refusals, never the backend's text (CONTRACTS 10.1 codes).</summary>
        public const string DownloadFailed = "Pobieranie nie powiodło się.";
    }

    // ---- suspicious pairs (copied from web/src/components/admin/suspicious/strings.ts)
    public static class Suspicious
    {
        public const string Title = "Podejrzane pary", MetaTitle = "Podejrzane pary | Gabinety Tech";
        public const string Intro = "Pary klient–programista z więcej niż dwiema opłaconymi rezerwacjami i wyłącznie opiniami z oceną 5/5. To sygnał do sprawdzenia, nie dowód.";
        public const string Unavailable = "Lista jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Empty = "Brak podejrzanych par na tej stronie.", Next = "Dalej";
        public const string TableLabel = "Podejrzane pary", ColClient = "Klient", ColPractitioner = "Programista", ColBookings = "Opłacone rezerwacje", ColReviews = "Opinie (wszystkie 5/5)";
    }
}
