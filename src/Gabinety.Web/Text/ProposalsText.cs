namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of contract availability and collaboration proposals (ADR proposal 0013). One word per concept (DESIGN-CONTRACT section 8): <b>propozycja</b> is what a client sends to a programmer
/// (never "oferta": that is the programmer's consultation listing), <b>współpraca</b> is the contract work a programmer advertises. The assistant's "Propozycja opisu" in the booking form is another thing and
/// keeps its own hooks.
/// </summary>
public static class ProposalsText
{
    public const string Propose = "Zaproponuj współpracę", ProposeLong = "Zaproponuj współpracę długoterminową", MyProposals = "Moje propozycje";

    /// <summary>The line on a profile and a card, and the filter of the list.</summary>
    public static class Availability
    {
        public const string Heading = "Współpraca kontraktowa", Aria = "Dostępność do współpracy";
        public const string Now = "Dostępny do współpracy od zaraz", From = "Dostępny do współpracy {0}", Hours = "{0} godz. tygodniowo", Rate = "Orientacyjna stawka: {0} za godzinę";
        public const string CardNow = "Dostępny do kontraktu", CardFrom = "Kontrakt: {0}";
        public const string Intro = "Ten programista przyjmuje propozycje współpracy. Przedstaw swoje warunki, a on odpowie przyjęciem, kontrpropozycją albo odmową. Platforma nie pobiera za to opłat.";
        public const string FilterTitle = "Współpraca", FilterAny = "Wszyscy", FilterContract = "Dostępni do kontraktu";

        // the relative phrase: "od zaraz", "za 3 dni", "za 2 tygodnie", "za 3 miesiące", "od stycznia 2027"
        public const string PhraseNow = "od zaraz", Day1 = "za 1 dzień", Days = "za {0} dni";
        public const string Week1 = "za 1 tydzień", WeeksFew = "za {0} tygodnie", WeeksMany = "za {0} tygodni";
        public const string Month1 = "za 1 miesiąc", MonthsFew = "za {0} miesiące", MonthsMany = "za {0} miesięcy", FromMonth = "od {0} {1}";
    }

    /// <summary>The practitioner's own form (profile page).</summary>
    public static class Contract
    {
        // "Ustaw dostępność", not "Zapisz ...": the journeys click the profile's button by the name "Zapisz" (a substring match), so no other button of the page may contain it.
        public const string Title = "Współpraca kontraktowa", Intro = "Powiedz, czy przyjmujesz propozycje współpracy. Na profilu pojawi się informacja o dostępności i przyciski „Zaproponuj współpracę”. Szczegóły ustalasz z klientem w propozycji, a umowę zawieracie poza platformą.";
        public const string StatusLabel = "Dostępność", NotAvailable = "Nie przyjmuję propozycji", AvailableNow = "Jestem dostępny od zaraz", AvailableFrom = "Będę dostępny od wskazanego dnia";
        public const string FromLabel = "Dostępny od", FromHint = "Dzień po dzisiejszym, najdalej za 24 miesiące.";
        public const string HoursLabel = "Godzin tygodniowo", HoursHint = "Od 1 do 60.";
        public const string RateLabel = "Orientacyjna stawka za godzinę (zł)", RateHint = "Opcjonalnie, od 20 do 1000 zł. To nie cennik: warunki ustalasz z klientem.";
        public const string Save = "Ustaw dostępność", CurrentNone = "Teraz nie przyjmujesz propozycji współpracy.";
        public const string Inbox = "Propozycje, które dostałeś, znajdziesz w zakładce „Propozycje”.";
        public const string ErrStatus = "Wybierz jedną z opcji.", ErrFrom = "Podaj dzień po dzisiejszym, najdalej za 24 miesiące.", ErrHours = "Podaj liczbę godzin od 1 do 60.", ErrRate = "Podaj stawkę od 20 do 1000 zł, na przykład 150 lub 150,50.";
        public const string ErrGeneral = "Nie udało się zapisać dostępności. Spróbuj ponownie za chwilę.", Unavailable = "Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.";
    }

    /// <summary>The terms of a proposal, in a list row and in the detail.</summary>
    public static class Terms
    {
        public const string Rate = "Stawka", RatePerHour = "{0} za godzinę", HoursPerWeek = "Wymiar", HoursPerWeekValue = "{0} godz. tygodniowo", TotalHours = "Liczba godzin", TotalHoursValue = "{0} godz. łącznie";
        public const string Duration = "Okres", DurationValue = "{0} mies.", Start = "Start", Kind = "Rodzaj", Short = "Krótka współpraca", Long = "Współpraca długoterminowa";
        public const string Summary = "{0} · {1} · start {2}", SummaryLong = "{0} · {1} · {2} · start {3}";
    }

    public static class Status
    {
        public const string AwaitingPractitioner = "Czeka na programistę", AwaitingClient = "Czeka na klienta", Accepted = "Zaakceptowana", Rejected = "Odrzucona", Withdrawn = "Wycofana", Expired = "Wygasła", Unknown = "Nieznany stan";
        public const string YourTurn = "Twoja kolej", Waiting = "Czekasz na odpowiedź", ExpiresAt = "Wygasa {0}", Round = "Runda {0}";
    }

    /// <summary>The client's form <c>/propozycja/{slug}</c>.</summary>
    public static class Form
    {
        public const string MetaTitle = "Propozycja współpracy | Gabinety Tech", TitleShort = "Propozycja współpracy dla {0}", TitleLong = "Propozycja współpracy długoterminowej dla {0}";
        public const string Intro = "Opisz, czego szukasz i na jakich warunkach. Programista odpowie przyjęciem, kontrpropozycją albo odmową. Dane kontaktowe wymienicie dopiero po wzajemnej akceptacji, a umowę zawrzecie poza platformą. Platforma nie pobiera za to opłat.";
        public const string KindLabel = "Rodzaj współpracy", KindShort = "Krótka (liczba godzin)", KindLong = "Długoterminowa (godziny w tygodniu przez kilka miesięcy)";
        public const string RateLabel = "Stawka za godzinę (zł)", RateHint = "Od 20 do 1000 zł, na przykład 150 lub 150,50.";
        public const string HoursPerWeekLabel = "Godzin tygodniowo", HoursPerWeekHint = "Od 1 do 60.";
        public const string TotalHoursLabel = "Liczba godzin", TotalHoursHint = "Od 1 do 400.";
        public const string DurationLabel = "Okres (miesiące)", DurationHint = "Od 1 do 36.", DurationHintOptional = "Opcjonalnie, od 1 do 36.";
        public const string StartLabel = "Start", StartHint = "Od dzisiaj, najdalej za 24 miesiące.";
        public const string TextLabel = "Wiadomość do programisty", TextHint = "Od 20 do 1000 znaków. Bez adresów e-mail, numerów telefonu i odnośników do komunikatorów.";
        public const string CompanyLabel = "Nazwa firmy", CompanyHint = "Opcjonalnie. Programista zobaczy ją od razu; bez niej zobaczy Cię jako „Klient” z pierwszą literą.";
        public const string Submit = "Wyślij propozycję", Back = "Wróć do profilu", Counter = "{0} / {1}";
        public const string NotAccepting = "Ten programista nie przyjmuje teraz propozycji współpracy.", NotFound = "Nie znaleziono takiego programisty.";
        public const string ErrorsOne = "Popraw 1 pole", ErrorsFew = "Popraw {0} pola", ErrorsMany = "Popraw {0} pól";
    }

    /// <summary>What is said about a field that failed our own check or the server's.</summary>
    public static class Errors
    {
        public const string Rate = "Podaj stawkę od 20 do 1000 zł, na przykład 150 lub 150,50.", HoursPerWeek = "Podaj liczbę godzin tygodniowo od 1 do 60.", TotalHours = "Podaj liczbę godzin od 1 do 400.";
        public const string Duration = "Podaj okres w miesiącach, od 1 do 36.", Start = "Podaj dzień startu: od dzisiaj, najdalej za 24 miesiące.";
        public const string TextShort = "Napisz co najmniej 20 znaków.", TextLong = "Wiadomość może mieć najwyżej 1000 znaków.", CompanyLong = "Nazwa firmy może mieć najwyżej 120 znaków.";
        public const string Contact = "Usuń adresy e-mail, numery telefonu, nazwy użytkowników i odnośniki do komunikatorów. Dane kontaktowe wymienicie po wzajemnej akceptacji.";
        public const string Network = "Nie udało się połączyć z serwisem. Spróbuj ponownie za chwilę.", Unavailable = "Ta część serwisu jest chwilowo niedostępna. Spróbuj ponownie za chwilę.";
        public const string Validation = "Sprawdź wpisane dane i spróbuj ponownie.", Generic = "Coś poszło nie tak. Spróbuj ponownie za chwilę.", Forbidden = "Nie masz uprawnień do tej czynności.", NotFound = "Nie znaleziono takiej propozycji.";
        public const string OwnProfile = "Nie możesz złożyć propozycji samemu sobie.", NotAvailable = "Ten programista nie przyjmuje teraz propozycji współpracy.";
        public const string Exists = "Masz już otwartą propozycję dla tego programisty. Odpowiedz w niej albo ją wycofaj.", RateLimited = "Wysłano zbyt wiele propozycji. Spróbuj ponownie później.";
        public const string InvalidTransition = "Propozycja zmieniła się w międzyczasie. Odśwież stronę i sprawdź jej aktualny stan.", SignedOut = "Twoja sesja wygasła.", SignInAgain = "Zaloguj się ponownie";
    }

    /// <summary>The lists: <c>/moje/propozycje</c> and the practitioner's tab.</summary>
    public static class List
    {
        public const string Title = "Moje propozycje", MetaTitle = "Moje propozycje | Gabinety Tech", TitlePractitioner = "Propozycje współpracy", MetaTitlePractitioner = "Propozycje współpracy | Gabinety Tech";
        public const string TabsLabel = "Stan propozycji", Open = "Otwarte", Closed = "Zamknięte", All = "Wszystkie";
        public const string EmptyTitle = "Brak propozycji", EmptyClient = "Gdy wyślesz propozycję współpracy programiście, zobaczysz ją tutaj.", EmptyPractitioner = "Gdy klient wyśle Ci propozycję współpracy, zobaczysz ją tutaj.";
        public const string EmptyFiltered = "W tym stanie nie ma żadnych propozycji.", FindPractitioner = "Znajdź programistę", ShowAll = "Pokaż wszystkie", Next = "Pokaż kolejne propozycje";
        public const string Details = "Szczegóły", Retry = "Spróbuj ponownie", Party = "{0} · {1}";
        public const string IntroPractitioner = "Propozycje od klientów. Odpowiedz na te, które czekają na Ciebie.", SettingsLink = "Ustaw dostępność do współpracy";
    }

    /// <summary>The detail page and its actions.</summary>
    public static class Detail
    {
        public const string Title = "Propozycja współpracy", MetaTitle = "Propozycja współpracy | Gabinety Tech", Back = "Wróć do listy propozycji", Reload = "Odśwież stronę";
        public const string Parties = "Strony", PartyPractitioner = "Programista", PartyClient = "Klient", Company = "Firma", Terms = "Aktualne warunki", Thread = "Przebieg negocjacji";
        public const string Contact = "Dane kontaktowe", ContactIntro = "Propozycja została zaakceptowana przez obie strony. Dalsze ustalenia i umowę załatwiacie poza platformą.", ContactEmail = "Adres e-mail drugiej strony";
        public const string ContactMissing = "Adres drugiej strony jest teraz niedostępny. Odśwież stronę za chwilę.";
        public const string Closed = "Ta propozycja jest zamknięta.", Waiting = "Czekasz na odpowiedź drugiej strony.", YourTurn = "To Twoja kolej: przyjmij warunki, zaproponuj inne albo odmów.";
    }

    public static class Thread
    {
        public const string RoundHeading = "Runda {0}: {1}", ByClient = "klient", ByPractitioner = "programista", BySystem = "system", NoText = "Bez wiadomości.", ErasedHint = "Wiadomość usunięta.";
        public const string Proposed = "Propozycja", Countered = "Kontrpropozycja", Accepted = "Przyjęcie warunków", Rejected = "Odmowa", Withdrawn = "Wycofanie", Expired = "Wygaśnięcie";
        public const string Label = "Rundy negocjacji";
    }

    public static class Actions
    {
        public const string Accept = "Przyjmij warunki", Counter = "Zaproponuj inne warunki", Reject = "Odmów", Withdraw = "Wycofaj propozycję";
        public const string AcceptTitle = "Przyjąć te warunki?", AcceptBody = "Po przyjęciu obie strony zobaczą swoje adresy e-mail i dalej ustalają współpracę poza platformą.", AcceptConfirm = "Tak, przyjmuję", Keep = "Wróć";
        public const string RejectTitle = "Odmówić tej propozycji?", RejectBody = "Propozycja zostanie zamknięta, a druga strona dostanie powiadomienie.", RejectConfirm = "Tak, odmawiam";
        public const string WithdrawTitle = "Wycofać propozycję?", WithdrawBody = "Propozycja zostanie zamknięta. Druga strona nie dostanie powiadomienia, ale zobaczy, że ją wycofano.", WithdrawConfirm = "Tak, wycofuję";
        public const string CounterTitle = "Nowe warunki", CounterIntro = "Wpisz warunki, które proponujesz w odpowiedzi. Druga strona będzie mogła je przyjąć, zmienić albo odmówić.", CounterNote = "Wiadomość do drugiej strony", CounterNoteHint = "Opcjonalnie, do 1000 znaków. Bez danych kontaktowych.", CounterSubmit = "Wyślij kontrpropozycję";
        public const string CounterCapped = "Osiągnięto limit kontrpropozycji w tym wątku. Możesz przyjąć warunki albo odmówić.";
    }

    /// <summary>The flash after an action, by the fixed key of <c>?done=</c>.</summary>
    public static class Done
    {
        public const string Sent = "Propozycja została wysłana. Programista dostanie powiadomienie.", Accepted = "Warunki zostały przyjęte. Adres drugiej strony widzisz poniżej.", Countered = "Kontrpropozycja została wysłana.";
        public const string Rejected = "Propozycja została odrzucona.", Withdrawn = "Propozycja została wycofana.";
    }
}
