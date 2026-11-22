namespace Gabinety.Web.Text;

/// <summary>The Polish text of the client area: my bookings, the booking detail with its actions (cancel, dispute, no-show), payment lines. The journeys (J3, J5, J6) read many of these strings: verbatim from <c>web/src/components/bookings/strings.ts</c>.</summary>
public static class BookingsText
{
    public const string DetailTitle = "Szczegóły rezerwacji", DetailMetaTitle = "Szczegóły rezerwacji | Gabinety Tech";
    public const string TabsLabel = "Rezerwacje jako", Details = "Szczegóły", Back = "Wróć do listy rezerwacji", Review = "Wystaw opinię";
    public const string Loading = "Ładowanie rezerwacji…", NotFound = "Nie znaleziono takiej rezerwacji.";
    public const string EmptyTitle = "Brak rezerwacji", EmptyClient = "Gdy zarezerwujesz konsultację, zobaczysz ją tutaj.", EmptyPractitioner = "Gdy klient zarezerwuje Twoją konsultację, zobaczysz ją tutaj.", EmptyFiltered = "Brak rezerwacji o tym statusie.";
    public const string FindPractitioner = "Znajdź programistę", ShowAll = "Pokaż wszystkie statusy", Next = "Pokaż kolejne rezerwacje";
    public const string ReloadHint = "Odśwież stronę", Retry = "Spróbuj ponownie";
    public const string PartyPractitioner = "Programista", PartyClient = "Klient";

    public static class Detail
    {
        public const string When = "Termin", Duration = "Czas trwania", Minutes = "{0} min", Zone = "Godziny w czasie polskim (Europe/Warsaw).", Status = "Status", Price = "Cena", Payment = "Płatność";
        public const string Refunded = "Zwrócono: {0}", Fee = "Prowizja", Payout = "Wypłata dla programisty";
        public const string Brief = "Opis problemu (klient)", BriefOwn = "Twój opis problemu", Join = "Dołącz do spotkania";
        public const string MeetingHint = "Otwórz go w terminie konsultacji.";
        public const string CompletedLine = "Konsultacja się odbyła.", RefundLine = "Zwrot za tę rezerwację: {0}.", NoRefundLine = "Rezerwacja anulowana mniej niż 24 godziny przed terminem: bez zwrotu.", Rebook = "Zarezerwuj ponownie";
    }

    public static class Payment
    {
        public const string Created = "Płatność rozpoczęta", Succeeded = "Opłacona", Failed = "Płatność nieudana", Refunded = "Zwrócona w całości", PartiallyRefunded = "Zwrócona częściowo", Unknown = "Stan płatności nieznany";
    }

    public static class Actions
    {
        public const string Cancel = "Anuluj rezerwację", Dispute = "Zgłoś spór", NoShow = "Zgłoś nieobecność klienta";
    }

    public static class Cancel
    {
        public const string Title = "Czy na pewno anulować rezerwację?";
        /// <summary>The rule of CONTRACTS 9.2, said when the booking does not carry its own outcome (F-11): the page never computes the outcome itself.</summary>
        public const string ClientRule = "Zwrot zależy od terminu: jeśli do rozpoczęcia konsultacji zostało co najmniej 24 godziny, zwrot wynosi 100% ceny; jeśli mniej niż 24 godziny, brak zwrotu.";
        public const string PractitionerRule = "Jako programista anulujesz zawsze z pełnym zwrotem: klient dostanie z powrotem 100% ceny, a Ty nie otrzymasz wypłaty za tę konsultację.";
        public const string OutcomeFull = "Otrzymasz zwrot {0}.", OutcomeNone = "Do konsultacji zostało mniej niż 24 godziny: nie otrzymasz zwrotu.";
        public const string OutcomePartial = "Otrzymasz zwrot {0} z {1}.";
        public const string PractitionerOutcome = "Klient dostanie zwrot {0}, a Ty nie otrzymasz wypłaty za tę konsultację.";
        public const string ReasonLabel = "Powód (opcjonalnie)", ReasonHint = "Najwyżej 500 znaków.", Confirm = "Tak, anuluj rezerwację", Keep = "Nie anuluj", Done = "Rezerwacja została anulowana.";
        public const string ReasonTooLong = "Powód może mieć najwyżej 500 znaków.";
    }

    public static class Dispute
    {
        public const string Title = "Zgłoszenie sporu", Intro = "Wypłata dla programisty zostanie wstrzymana do rozstrzygnięcia przez administratora.", KindLabel = "Rodzaj sporu";
        public const string NotAsDescribed = "Konsultacja różniła się od opisu", PractitionerNoShow = "Programista się nie stawił", Other = "Inny powód";
        public const string TextLabel = "Opis sporu", TextHint = "Od 10 do 1000 znaków. Opisz, co się stało; administrator zobaczy ten opis.";
        public const string TooShort = "Opis musi mieć co najmniej 10 znaków.", TooLong = "Opis może mieć najwyżej 1000 znaków.", KindInvalid = "Wybierz rodzaj sporu.";
        public const string Submit = "Wyślij zgłoszenie", Close = "Zamknij", Done = "Spór został zgłoszony. Wypłata dla programisty jest wstrzymana do rozstrzygnięcia.";
        public const string Counter = "{0} / 1000";
    }

    public static class NoShow
    {
        public const string Title = "Zgłosić nieobecność klienta?", Body = "Konsultacja zostanie rozliczona jak odbyta. Tego nie można cofnąć.", Confirm = "Tak, zgłoś nieobecność", Keep = "Nie zgłaszaj", Done = "Nieobecność klienta została zgłoszona.";
    }

    public static class Errors
    {
        public const string InvalidTransition = "To nie jest już możliwe: stan rezerwacji się zmienił. Odśwież stronę.", Refresh = "Odśwież";
        public const string Unavailable = "Usługa rezerwacji jest chwilowo niedostępna. Spróbuj ponownie za chwilę.", Validation = "Sprawdź wypełnione pola i spróbuj ponownie.";
        public const string Forbidden = "Nie masz dostępu do tej rezerwacji.", RateLimited = "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.";
        public const string Network = "Nie udało się połączyć z usługą. Spróbuj ponownie.", Generic = "Coś poszło nie tak. Spróbuj ponownie.";
        public const string SignedOut = "Sesja wygasła. Zaloguj się ponownie.", SignIn = "Zaloguj się";
    }
}
