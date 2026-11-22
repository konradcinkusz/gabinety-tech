namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of the booking flow: the form, the checkout and the confirmation (SPEC 5 rule 1: one place per area). Strings the journeys read (J3, J4) are copied verbatim from
/// <c>web/src/components/booking/strings.ts</c>; the rest follows DESIGN-CONTRACT 5.5 and 5.6 and the copy rules of section 8.
/// </summary>
public static class BookFlowText
{
    public const string Title = "Rezerwacja konsultacji", MetaTitle = "Rezerwacja konsultacji | Gabinety Tech";
    public const string MissingSlot = "Nie wybrano terminu albo adres jest niepełny. Wybierz termin na stronie gabinetu.";
    public const string BackToSearch = "Wróć do wyszukiwarki", ToProfile = "Wróć do profilu i wybierz termin", ProfileUnavailable = "Nie udało się teraz wczytać oferty. Spróbuj ponownie za chwilę.";
    public const string Retry = "Spróbuj ponownie";

    public static class Summary
    {
        public const string Title = "Podsumowanie", Offering = "Konsultacja", Practitioner = "Programista", When = "Termin", Duration = "Czas trwania", Minutes = "{0} min", Price = "Do zapłaty";
        public const string Zone = "Godziny w czasie polskim (Europe/Warsaw).";
        public const string Hold = "Termin jest wstrzymany na 10 minut od kliknięcia „Zapłać”.";
        public const string ChangeTime = "Zmień termin";
        /// <summary>The free-cancellation line (UX-020, finding 6): the deadline is the start minus the full-refund window; {0} is "poniedziałek, 7 grudnia, 12:00".</summary>
        public const string FreeUntil = "Bezpłatne anulowanie do {0}.";
        /// <summary>Shown instead when the deadline has already passed; {0} is the window in hours.</summary>
        public const string NoFreeCancel = "Do konsultacji zostało mniej niż {0} godz.: rezygnacja nie da już zwrotu.";
        public const string Meeting = "Link do spotkania";
        public const string MeetingHint = "Otwórz go w terminie konsultacji.";
    }

    /// <summary>The four steps of the booking (<c>BookingSteps</c>); the checkout and the confirmation show the same list with another current step.</summary>
    public static class Steps
    {
        public const string Label = "Kroki rezerwacji", Done = "(ukończony)";
        public static readonly string[] Names = ["Termin", "Opis", "Płatność", "Gotowe"];
    }

    public static class Brief
    {
        public const string Label = "Opisz problem", Hint = "Od 20 do 2000 znaków. Opis zobaczy programista po opłaceniu rezerwacji.";
        public const string TooShort = "Opis musi mieć co najmniej 20 znaków.", TooLong = "Opis może mieć najwyżej 2000 znaków.";
        public const string Counter = "{0} / 2000";
    }

    public static class Errors
    {
        /// <summary>Heading of the form summary (DESIGN-CONTRACT 5.5): "Popraw 1 pole", "Popraw 2 pola", "Popraw 5 pól".</summary>
        public const string SummaryOne = "Popraw 1 pole", SummaryFew = "Popraw {0} pola", SummaryMany = "Popraw {0} pól";
        public const string Unavailable = "Nie udało się połączyć z usługą. Spróbuj ponownie: nie zostaniesz obciążony dwa razy.";
        public const string RateLimited = "Zbyt wiele prób. Odczekaj chwilę i spróbuj ponownie.";
        public const string SlotTaken = "Ten termin został już zajęty albo nie jest dostępny. Wybierz inny termin.";
        public const string Inactive = "Ta konsultacja nie jest już dostępna.", Own = "Nie możesz zarezerwować własnej konsultacji.", NotBookable = "Ten programista nie przyjmuje teraz rezerwacji.";
        /// <summary>422 <c>practitioner_payments_not_ready</c> (ADR 0011): the practitioner has no connected payment account that can receive money yet.</summary>
        public const string PaymentsNotReady = "Ten programista nie może jeszcze przyjmować płatności, więc nie można zarezerwować tej konsultacji. Spróbuj z innym programistą albo wróć za jakiś czas.";
        public const string Generic = "Nie udało się utworzyć rezerwacji. Sprawdź dane i spróbuj ponownie.";
        public const string BadTarget = "Nie udało się przejść do płatności. Spróbuj ponownie: nie zostaniesz obciążony dwa razy.";
    }

    public static class Assist
    {
        public const string Ask = "Pomóż mi opisać problem", Note = "Propozycja to tylko podpowiedź: możesz ją zmienić i nic nie zostanie dodane bez Twojego kliknięcia.";
        public const string NeedsText = "Najpierw opisz problem w co najmniej 20 znakach, wtedy asystent będzie mógł pomóc.";
        public const string Proposal = "Propozycja opisu (możesz ją edytować)", Questions = "Warto jeszcze opisać", Title = "Propozycja asystenta", Category = "Sugerowana kategoria: {0}", CategoryLabel = "Sugerowana kategoria:";
        public const string Apply = "Dodaj propozycję do opisu", Applied = "Propozycja została dodana do opisu.";
        public const string Unavailable = "Asystent jest chwilowo niedostępny. Opisz problem samodzielnie i zarezerwuj termin.";
        public const string RateLimited = "Zbyt wiele próśb do asystenta. Spróbuj za chwilę albo opisz problem samodzielnie.";
        public const string Failed = "Asystent nie mógł przygotować propozycji. Opisz problem samodzielnie.";
    }

    public static class Form
    {
        public const string Pay = "Zapłać {0}";
    }

    public static class Checkout
    {
        public const string Title = "Płatność (tryb demonstracyjny)", MetaTitle = "Płatność (tryb demonstracyjny) | Gabinety Tech";
        public const string Demo = "To tryb demonstracyjny: płatność jest symulowana i żadne pieniądze nie są pobierane.";
        public const string Succeed = "Symuluj udaną płatność", Fail = "Symuluj nieudaną płatność";
        public const string Hold = "Termin jest wstrzymany jeszcze przez", HoldLabel = "Czas na płatność";
        public const string AnnounceFive = "Zostało 5 minut na płatność.", AnnounceOne = "Została 1 minuta na płatność.", Expired = "Czas na płatność minął, termin został zwolniony. Wybierz termin ponownie.";
        public const string Failed = "Płatność nie powiodła się. Rezerwacja nie została potwierdzona.";
        public const string NotMock = "Symulacja płatności jest wyłączona w tym środowisku.", Error = "Symulacja nie powiodła się. Spróbuj ponownie.";
        public const string Closed = "Ta rezerwacja nie oczekuje już na płatność.", ToBooking = "Zobacz rezerwację", ToProfile = "Wróć do profilu", ToSearch = "Wróć do wyszukiwarki";
        public const string LoadFailed = "Nie udało się wczytać rezerwacji. Odśwież stronę.", Refresh = "Odśwież stronę";
        public const string Declined = "Płatność nie została dokończona.";
    }

    public static class Confirmation
    {
        public const string Title = "Rezerwacja", MetaTitle = "Rezerwacja | Gabinety Tech";
        /// <summary>The h1 of a confirmed booking (UX-021); the other states keep <see cref="Title"/> so no page claims success it does not have.</summary>
        public const string ConfirmedTitle = "Rezerwacja potwierdzona";
        public const string Confirmed = "Rezerwacja potwierdzona. Dziękujemy!", Pending = "Czekamy na potwierdzenie płatności.", Other = "Ta rezerwacja nie jest aktywna.";
        public const string Refresh = "Odśwież", View = "Zobacz rezerwację", ToSearch = "Wróć do wyszukiwarki";
        public const string Calendar = "Dodaj do kalendarza";
        /// <summary>The "Co dalej" list under the summary of a confirmed booking.</summary>
        public const string NextTitle = "Co dalej", NextEmail = "Wyślemy e-mail ze szczegółami rezerwacji.", NextMeeting = "Link do spotkania otwórz w terminie konsultacji.";
        public const string NextCancel = "Rezerwację możesz anulować w zakładce Moje rezerwacje. Zasady zwrotu znajdziesz w", NextCancelLink = "cenniku";
        /// <summary>The calendar file's text (RFC 5545 DESCRIPTION): the practitioner's name, and the meeting link when it is safe.</summary>
        public const string CalendarPractitioner = "Programista: {0}", CalendarMeeting = "Link do spotkania: {0}";
    }
}
