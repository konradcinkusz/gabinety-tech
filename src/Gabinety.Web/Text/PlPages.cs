namespace Gabinety.Web.Text;

/// <summary>
/// The Polish text of the public pages (SPEC 5 rule 1), beside <see cref="Pl"/> for size: strings are verbatim from the Next.js catalogue (<c>web/src/components/static/strings.ts</c>,
/// <c>profile/strings.ts</c>, <c>slots/strings.ts</c>), so the journeys find the same names on either host. Improvements of the UI audit are marked with the finding id.
/// </summary>
public static class PlPages
{
    public static class HomeExtra
    {
        public const string MoreHow = "Szczegóły: jak to działa", RulesLink = "Zasady rankingu", PricingLink = "Cennik";

        // Hero: example queries, each a link to the list with that search (UX-011, V-1).
        public const string ExamplesLead = "Na przykład:";
        public const string ExampleAudit = "audyt kodu", ExampleMigration = "migracja .NET", ExampleAi = "integracja AI";

        // Category tiles: one distinct sentence per seeded problem (slug -> text), a generic sentence for a slug this map does not know, and the count in Polish plural forms.
        public static readonly IReadOnlyDictionary<string, string> CategoryDescriptions = new Dictionary<string, string>
        {
            ["audyt-kodu"] = "Przegląd istniejącego kodu: jakość, bezpieczeństwo i plan poprawek.",
            ["migracja-legacy-dotnet"] = "Przeniesienie starszej aplikacji .NET na nowszą wersję, krok po kroku.",
            ["integracja-ai"] = "Dołączenie modeli językowych i agentów AI do istniejącego systemu.",
            ["automatyzacja-procesow"] = "Zautomatyzowanie powtarzalnych zadań i przepływów danych.",
            ["architektura-systemu"] = "Ocena i projekt struktury systemu, granic usług i danych.",
            ["code-review"] = "Druga para oczu na zmiany w kodzie przed wdrożeniem.",
            ["wycena-projektu"] = "Oszacowanie zakresu, czasu i kosztów, zanim zaczniesz projekt.",
            ["rozwiazanie-awarii"] = "Pilna pomoc przy awarii: diagnoza, przywrócenie działania i wnioski.",
        };
        public const string CategoryFallback = "Konsultacja z programistą w obszarze: {0}.";
        public const string PractitionersOne = "{0} programista", PractitionersFew = "{0} programiści", PractitionersMany = "{0} programistów";

        // Trust band: the verified count is the real number from the sitemap; the statements are the existing ones.
        public const string TrustLabel = "Dlaczego warto";
        public const string VerifiedOne = "{0} zweryfikowany programista", VerifiedFew = "{0} zweryfikowani programiści", VerifiedMany = "{0} zweryfikowanych programistów";
        public const string TrustPayment = "Płatność z góry, programista dostaje pieniądze po konsultacji";
        public const string TrustRanking = "Ranking bez płatnych miejsc";

        // Featured row: a few practitioners from the first page of the list. A region that fails soft: no heading, no message, when there is nothing to show.
        public const string FeaturedTitle = "Zweryfikowani programiści", FeaturedIntro = "Kilka osób z listy. Pełna lista jest na stronie programistów.";
        public const string FeaturedFrom = "od {0}";

        // Call to action for programmers.
        public const string ForPractitionersTitle = "Jesteś programistą?";
        public const string ForPractitionersBody = How.P1 + " " + How.P2;
        public const string ForPractitionersLink = "Dołącz jako programista";

        // Questions and answers. Every rule is the wording of the Pricing, How and Pl.Home texts; no amount is written here: the price list is /cennik.
        public const string FaqTitle = "Pytania i odpowiedzi";
        public const string FaqPayWhenQ = "Kiedy płacę?";
        public const string FaqPayWhenA = "Płacisz z góry, przy rezerwacji. Termin jest zarezerwowany po potwierdzeniu płatności. Programista dostaje pieniądze dopiero po konsultacji.";
        public const string FaqCancelQ = "Czy mogę odwołać konsultację?";
        public const string FaqCancelA = How.Pay1 + " " + How.Pay2;
        public const string FaqFeeQ = "Ile płacę platformie?";
        public const string FaqFeeA = Pricing.Intro + " Prowizja platformy dotyczy tylko pierwszej zrealizowanej konsultacji danej pary klient i programista. " + Pricing.Later;
        public const string FaqFeeLink = "Zobacz cennik";
        public const string FaqDisputeQ = "Co, jeśli konsultacja się nie odbyła?";
        public const string FaqDisputeA = How.Pay3;
    }

    public static class ListExtra
    {
        public const string Verified = "Zweryfikowany";
    }

    public static class How
    {
        public const string MetaTitle = "Jak to działa | Gabinety Tech", MetaDescription = "Od wyboru programisty po opinię: kolejne kroki płatnej konsultacji online, płatności i zwroty.";
        public const string Title = "Jak to działa", Intro = "Gabinety Tech łączy klientów z programistami, z którymi można umówić płatną konsultację online.";
        public const string ClientTitle = "Dla klienta";
        public const string C1 = "Wybierz problem i technologię na liście programistów albo wyszukaj po słowie.";
        public const string C2 = "Sprawdź profil: oferty, ceny, opinie i plakietki weryfikacji.";
        public const string C3 = "Wybierz wolny termin i opisz problem. Opis jest widoczny tylko dla programisty.";
        public const string C4 = "Zapłać z góry. Termin jest zarezerwowany po potwierdzeniu płatności.";
        public const string C5 = "Odbądź konsultację online. Po jej zakończeniu możesz wystawić opinię.";
        public const string PaymentTitle = "Płatności i zwroty";
        public const string Pay1 = "Anulowanie co najmniej 24 godziny przed terminem oznacza pełny zwrot.";
        public const string Pay2 = "Późniejsze anulowanie nie oznacza zwrotu.";
        public const string Pay3 = "Jeśli konsultacja się nie odbyła albo przebiegła inaczej, niż ustalono, możesz zgłosić spór w ciągu 48 godzin od jej końca. Rozstrzyga go administrator.";
        public const string PractitionerTitle = "Dla programisty";
        public const string P1 = "Załóż konto, uzupełnij profil i ofertę, a potem ustaw dostępność.";
        public const string P2 = "Administrator sprawdza profil. Po zatwierdzeniu pojawiasz się na liście.";
        public const string P3 = "Wypłata następuje 48 godzin po zakończeniu konsultacji, o ile nikt nie zgłosił sporu.";
        public const string SeeAlso = "Zobacz też";
        public const string CtaTitle = "Gotowy, żeby zacząć?", CtaBody = "Wybierz zweryfikowanego programistę i wolny termin. Płacisz z góry, a termin jest zarezerwowany po potwierdzeniu płatności.", Cta = "Zobacz programistów";
    }

    public static class Ranking
    {
        public const string MetaTitle = "Zasady rankingu | Gabinety Tech", MetaDescription = "Jak ustalamy kolejność programistów: wagi, stałe i przykład obliczenia. Nie da się za nią zapłacić.";
        public const string Title = "Zasady rankingu", Intro = "Kolejność programistów na listach wynika z jednego wzoru. Publikujemy go w całości razem z wersją i datą obowiązywania.";
        public const string Version = "Wersja zasad: {0}", EffectiveFrom = "Obowiązuje od: {0}";
        public const string NothingPaid = "Za miejsce w rankingu nie można zapłacić: żadna opłata, plan ani promocja nie jest daną wejściową wzoru.";
        public const string TieBreak = "Przy równej liczbie punktów wcześniej zatwierdzony profil jest wyżej.";
        public const string WeightsTitle = "Wagi", WeightsCaption = "Składniki wyniku i ich wagi";
        public const string ColPart = "Składnik", ColWeight = "Waga", ColMeaning = "Co mierzy";
        public const string Rating = "Opinie", Reliability = "Rzetelność", Availability = "Dostępność", Completeness = "Kompletność profilu";
        public const string RatingMeaning = "Średnia ocena z opublikowanych opinii, skorygowana tak, by mała próba nie dawała skrajnego wyniku.";
        public const string ReliabilityMeaning = "Odsetek zrealizowanych konsultacji w stosunku do odwołanych przez programistę i niestawiennictw.";
        public const string AvailabilityMeaning = "Liczba wolnych terminów w najbliższych 14 dniach.";
        public const string CompletenessMeaning = "Jak dokładnie wypełniony jest profil.";
        public const string ScoreLine = "Wynik to suma składników pomnożonych przez wagi, przeliczona na skalę od 0 do 100.";
        public const string ConstantsTitle = "Stałe", ConstantsCaption = "Stałe używane we wzorze", ColConstant = "Stała", ColValue = "Wartość";
        public const string ConstM = "Waga założenia wstępnego (liczba fikcyjnych opinii)", ConstMu = "Ocena założenia wstępnego";
        public const string ConstMin = "Najmniej opinii, od których pokazujemy średnią ocenę", ConstSlots = "Liczba wolnych terminów, od której dostępność daje pełne punkty";
        public const string SmallSample = "Poniżej tylu opinii przy profilu widać napis „Nowy”, a nie średnią: mała próba nie jest średnią.";
        public const string ExampleTitle = "Przykład obliczenia";
        public const string ExampleIntro = "Programista z {0} opiniami o średniej {1}, {2} zrealizowanymi konsultacjami, {3} odwołaną przez siebie, profilem wypełnionym w {4} i {5} wolnymi terminami w najbliższych 14 dniach.";
        public const string ExampleCaption = "Obliczenie przykładowego wyniku", ColInput = "Wartość składnika (0 do 1)", ColContribution = "Punkty", ExampleTotal = "Wynik razem";
        public const string ScrollRegion = "Tabela: {0}. Przewijana poziomo.";
        public const string Unavailable = "Nie udało się teraz wczytać zasad rankingu. Pokazujemy je zawsze z aktualnych danych, więc spróbuj ponownie za chwilę.";
    }

    public static class Pricing
    {
        public const string MetaTitle = "Cennik | Gabinety Tech", MetaDescription = "Klient płaci cenę z oferty. Prowizja platformy dotyczy tylko pierwszej konsultacji danej pary klient i programista.";
        public const string Title = "Cennik", Intro = "Klient płaci cenę z oferty programisty, bez dodatkowych opłat.";
        public const string Rule = "Prowizja platformy to {0} ceny pierwszej zrealizowanej konsultacji danej pary klient i programista.";
        public const string OnlyFirst = "Prowizja dotyczy: tylko pierwsza konsultacja.";
        public const string Later = "Za kolejne konsultacje tej samej pary platforma nie pobiera nic.";
        public const string Absorbed = "Koszty operatora płatności pokrywa platforma.";
        public const string Rounding = "Prowizję zaokrąglamy do pełnego grosza w górę od połowy.";
        public const string ExampleTitle = "Przykład", ExampleCaption = "Podział ceny pierwszej konsultacji", ColItem = "Pozycja", ColAmount = "Kwota";
        public const string Price = "Cena konsultacji", FeeRow = "Prowizja platformy", Payout = "Dla programisty";
        public const string RefundNote = "Po pełnym zwrocie prowizji nie ma. Przy częściowym zwrocie liczymy ją od kwoty, która zostaje.";
        public const string Example2Title = "Drugi przykład", Example2Intro = "Ta sama zasada dla tańszej konsultacji: prowizja liczona od ceny i zaokrąglana do pełnego grosza.";
        public const string Example2Caption = "Podział ceny konsultacji {0}", FindLink = "Zobacz programistów i ich ceny";
    }

    public static class Legal
    {
        public const string TermsMetaTitle = "Regulamin | Gabinety Tech", TermsMetaDescription = "Regulamin serwisu Gabinety Tech.", TermsTitle = "Regulamin";
        public const string PrivacyMetaTitle = "Polityka prywatności | Gabinety Tech", PrivacyMetaDescription = "Polityka prywatności serwisu Gabinety Tech.", PrivacyTitle = "Polityka prywatności";
        public const string DraftNotice = "Wersja robocza: tekst nie został jeszcze zatwierdzony przez prawnika i nie obowiązuje.";
        public const string Version = "Wersja: {0}", Date = "Data: {0}", Contents = "Spis treści", Unavailable = "Nie udało się wczytać tekstu. Spróbuj ponownie za chwilę.";
    }

    public static class Profile
    {
        public const string MetaTitle = "{0}: konsultacje online | Gabinety Tech", MetaDescription = "{0}. {1} Umów płatną konsultację online.";
        public const string OfferingsTitle = "Oferta", AboutTitle = "O mnie", Duration = "{0} min", Book = "Zarezerwuj", BookOffering = "Zarezerwuj: {0}";
        public const string Portfolio = "Portfolio", Github = "GitHub", Completed = "Zrealizowane konsultacje: {0}", MemberSince = "Na platformie od {0}";
        public const string IsNew = "Nowy", Rating = "Ocena {0} ({1})", BadgesLabel = "Weryfikacje", FactsLabel = "Dane programisty", LinksLabel = "Linki";
        public const string NoOfferings = "Ten programista nie ma teraz aktywnych ofert.";
        public const string Email = "E-mail zweryfikowany", EmailInfo = "Adres e-mail tego programisty został potwierdzony.";
        public const string GithubBadge = "GitHub połączony", GithubInfo = "Konto GitHub należy do tego programisty: potwierdził to kodem w publicznym profilu.";
        public const string Nip = "NIP zweryfikowany", NipInfo = "Numer NIP działalności tego programisty został sprawdzony.";
        public const string Approved = "Zatwierdzony", ApprovedInfo = "Profil został sprawdzony i zatwierdzony przez zespół Gabinety Tech.";
        public const string ReviewsTitle = "Opinie", ReviewsNew = "Nowy na platformie: ten programista nie ma jeszcze opinii.", ReviewsEmpty = "Nie ma jeszcze opublikowanych opinii.";
        public const string ReviewsUnavailable = "Opinie są chwilowo niedostępne. Spróbuj za chwilę.", Retry = "Spróbuj ponownie", Reviewer = "Zweryfikowany klient";
        public const string ReviewRating = "Ocena: {0} z 5", ReviewsMore = "Pokaż więcej opinii", ReplyHeading = "Odpowiedź programisty", ReviewsSummary = "Średnia ocen {0} na podstawie opinii: {1}";
        // F-09, P-PROF-3: the 404 of a profile names what is missing.
        public const string NotFoundTitle = "Nie znaleziono programisty", NotFoundBody = "Ten profil nie istnieje albo programista nie jest już widoczny w serwisie.";
        public const string NotFoundAll = "Zobacz wszystkich programistów", NotFoundCategories = "Albo wybierz problem";
        public const string Unavailable = "Nie udało się teraz wczytać profilu. Spróbuj ponownie za chwilę.";
        // UX-014: header checks, summary panel, review summary.
        public const string ChecksTitle = "Co sprawdziliśmy", RatingShort = "★ {0} ({1})", TechLabel = "Technologie";
        public const string SummaryTitle = "Podsumowanie", PriceFromLabel = "od", DurationRange = "{0}–{1} min", SeeTimes = "Zobacz terminy";
        public const string PaymentNote = "Płatność z góry, programista dostaje pieniądze po konsultacji";
        public const string ReviewsSummaryLine = "★ {0} · {1} {2}", OpinionOne = "opinia", OpinionFew = "opinie", OpinionMany = "opinii";
    }

    public static class Slots
    {
        public const string Label = "Wolne terminy", PickDay = "Wybierz dzień", Times = "Godziny w dniu: {0}";
        // F-16 / P-PROF-2: no offers, no times and times-unavailable are three different sentences.
        public const string Empty = "Brak wolnych terminów w najbliższych {0} dniach. Zajrzyj później.";
        public const string Unavailable = "Nie możemy teraz pokazać terminów. Spróbuj ponownie za chwilę.";
        public const string Nearest = "Najbliższy wolny termin: {0}", Today = "dziś", Tomorrow = "jutro";
        public const string Morning = "Rano", Afternoon = "Po południu", Evening = "Wieczorem", PrevWeek = "Poprzedni tydzień", NextWeek = "Następny tydzień", Week = "Tydzień";
        public const string More = "Pokaż kolejne dni", Retry = "Spróbuj ponownie", TimeZone = "Godziny w czasie polskim (Europe/Warsaw).",
            LocalNote = "Twoja strefa czasowa: {zone}. Na przykład {pl} w Polsce to {local} u Ciebie.", LocalTitle = "{local} w Twojej strefie ({zone})";
    }

    public static class Nav
    {
        public const string Breadcrumb = "Okruszki", Home = "Strona główna";
    }
}
