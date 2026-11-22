namespace Gabinety.DemoSeeder;

/// <summary>One offering. Categories are slots into the taxonomy read from the Directory (slot n is the n-th problem or tech of the list, wrapping), so no slug is hard-coded.</summary>
public sealed record DemoOffering(string Title, string Description, int Minutes, long PriceMinor, int ProblemSlot, int[] TechSlots);

/// <summary>The ranking inputs the internal pushes carry (CONTRACTS 3.6). Reviews and Average go to /reputation, the rest to /booking-stats.</summary>
public sealed record DemoStats(int Reviews, decimal Average, int Completed, int Cancellations, int NoShows, int OpenSlots);

public sealed record DemoPractitioner(int Number, string DisplayName, string Slug, DemoStatus Target, string Headline, string Bio,
    DemoOffering[] Offerings, DemoStats? Stats, string? Reason = null)
{
    /// <summary>demo-practitioner-1 to 8 exist in the DevIdentity; 9 and above are registered by the seeder.</summary>
    public string UserName => $"demo-practitioner-{Number}";
    public string GithubLogin => $"demo-dev-{Number}";
    public string PortfolioUrl => $"https://demo-{Number}.portfolio.example.com/";
    public string MeetingUrl => $"https://meet.jit.si/demo-gabinety-{Number}";

    /// <summary>A checksum-valid NIP in the synthetic range 9990xxxxxc. The Directory checks the checksum only; no registry says whether such a number is taken.</summary>
    public string Nip => Nips.FirstValidFrom(999_000_000 + Number * 100);
}

public static class Nips
{
    static readonly int[] Weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];

    /// <summary>The first nine-digit prefix at or above <paramref name="nineDigits"/> whose checksum digit exists (a remainder of 10 has none), with that digit appended.</summary>
    public static string FirstValidFrom(int nineDigits)
    {
        for (var n = nineDigits; ; n++)
        {
            var d = n.ToString("D9");
            var check = d.Select((c, i) => (c - '0') * Weights[i]).Sum() % 11;
            if (check != 10) return d + check;
        }
    }
}

/// <summary>
/// The demo directory: ten fictional practitioners, six Approved across four problem categories, one each Submitted, Draft, Suspended and Rejected.
/// Names are visibly fictional; texts are Polish and carry no e-mail, phone number or @handle (the Directory's contact-detail check) and no long digit runs.
/// The booking-side stats are chosen so the public order is not alphabetical; the review counts are not chosen at all, they are what the seeder creates (three reviews, one each on Anna, Bartosz and Ewa).
/// </summary>
public static class DemoCatalog
{
    public const int ClientCount = 4; // demo-client (seeded by DevIdentity) and demo-client-2 to 4

    public static IEnumerable<string> ClientNames => Enumerable.Range(1, ClientCount).Select(i => i == 1 ? "demo-client" : $"demo-client-{i}");

    /// <summary>
    /// The ranking inputs of the booking side. Reviews and Average are left empty here on purpose: <see cref="Practitioners"/> fills them from what the seeder really creates
    /// (<see cref="ReputationPlan"/> on the Completed bookings of <see cref="BookingPlan"/>), so a profile never shows more reviews than exist.
    /// </summary>
    static DemoStats Stats(int completed, int cancellations, int noShows, int openSlots) => new(0, 0m, completed, cancellations, noShows, openSlots);

    /// <summary>The reviews the seeder creates on one practitioner, as the rating summary Reputation would push for them: (count, mean).</summary>
    static (int Count, decimal Average) SeededReviews(DemoPractitioner p, DemoPractitioner[] approvedByKey)
    {
        if (p.Target != DemoStatus.Approved && p.Target != DemoStatus.Suspended) return (0, 0m);
        // the Booking step makes bookings with the Approved practitioners that have an offering, ordered by key (a Suspended one is not Approved when it runs)
        var ratings = ReputationPlan.All
            .Where(r => approvedByKey[BookingPlan.All.Single(b => b.Key == r.BookingKey).Practitioner % approvedByKey.Length].Number == p.Number)
            .Select(r => r.Rating).ToList();
        return ratings.Count == 0 ? (0, 0m) : (ratings.Count, (decimal)ratings.Sum() / ratings.Count);
    }

    static DemoPractitioner[] WithSeededReviews(DemoPractitioner[] raw)
    {
        var approved = raw.Where(p => p.Target == DemoStatus.Approved && p.Offerings.Length > 0).OrderBy(p => p.UserName, StringComparer.Ordinal).ToArray();
        return raw.Select(p =>
        {
            if (p.Stats is null) return p;
            var (count, average) = SeededReviews(p, approved);
            return p with { Stats = p.Stats with { Reviews = count, Average = average } };
        }).ToArray();
    }

    static readonly DemoPractitioner[] Raw =
    [
        new(1, "Anna Demowska", "demo-anna-demowska", DemoStatus.Approved,
            "Audyty kodu .NET i przegląd architektury przed wdrożeniem",
            "Od kilkunastu lat piszę i recenzuję systemy w .NET. Pomagam zespołom ocenić stan kodu przed wdrożeniem: wskazuję długi techniczne, ryzyka wydajnościowe i miejsca, w których architektura zaczyna przeszkadzać. Dostajesz konkretną listę zmian w kolejności opłacalności.",
            [new("Audyt kodu aplikacji .NET", "Przeglądam wskazany fragment repozytorium i omawiam z Tobą najważniejsze ryzyka oraz kolejność poprawek.", 60, 24000, 0, [0, 1]),
             new("Szybka ocena jakości pull requesta", "Krótka konsultacja nad jednym dużym pull requestem: czytelność, testy, ryzyka.", 30, 12000, 5, [1])],
            Stats(30, 1, 0, 6)),
        new(2, "Bartosz Testowy", "demo-bartosz-testowy", DemoStatus.Approved,
            "Migracje legacy .NET Framework do nowoczesnego .NET bez przestoju",
            "Specjalizuję się w stopniowych migracjach aplikacji z .NET Framework do nowoczesnego .NET. Pracuję metodą strangler: nowe moduły obok starych, bez wielkiego przepisywania. Na konsultacji ustalamy kolejność kroków, testy zabezpieczające i plan wycofania zmian.",
            [new("Plan migracji z .NET Framework", "Wspólnie rozpisujemy etapy migracji Twojej aplikacji, zależności do wymiany i testy, które muszą przejść po każdym etapie.", 60, 32000, 1, [0, 2]),
             new("Konsultacja: co zrobić z WCF i Web Forms", "Omawiamy opcje dla starych technologii: przepisać, opakować czy wyłączyć.", 30, 15000, 1, [0, 2])],
            Stats(8, 0, 0, 10)),
        new(3, "Celina Przykładowska", "demo-celina-przykladowska", DemoStatus.Approved,
            "Integracja modeli językowych z istniejącą aplikacją firmową",
            "Pomagam wpiąć modele językowe w aplikacje, które już działają: wyszukiwanie w dokumentach, asystenci dla obsługi klienta, streszczenia zgłoszeń. Na konsultacji przechodzimy od pomysłu do prostego, mierzalnego prototypu i oceniamy koszty oraz ryzyka. Dopiero zaczynam na platformie.",
            [new("Pierwszy prototyp z modelem językowym", "Szkicujemy architekturę prostej integracji, dobieramy model i sposób oceny jakości odpowiedzi.", 60, 18000, 2, [3, 7])],
            Stats(0, 0, 0, 4)),
        new(4, "Dariusz Fikcyjny", "demo-dariusz-fikcyjny", DemoStatus.Approved,
            "Automatyzacja powtarzalnych procesów w małej firmie, bez nadmiaru narzędzi",
            "Pomagam małym firmom pozbyć się ręcznej pracy między arkuszami, skrzynkami i systemem księgowym. Wybieram najprostsze narzędzia, które da się utrzymać bez zespołu programistów. Podczas konsultacji mapujemy proces, wskazujemy kroki do automatyzacji i szacujemy oszczędność czasu.",
            [new("Mapa procesu do automatyzacji", "Rysujemy jeden proces krok po kroku i wybieramy, co zautomatyzować w pierwszej kolejności.", 60, 14000, 3, [4])],
            Stats(5, 0, 0, 10)),
        new(5, "Ewa Wzorcowa", "demo-ewa-wzorcowa", DemoStatus.Approved,
            "Architektura systemów i decyzje projektowe, które da się obronić",
            "Pracuję z zespołami nad decyzjami architektonicznymi: podział na moduły, granice usług, dane i zgodność. Pomagam spisać decyzje tak, żeby za rok było wiadomo, dlaczego je podjęto. Konsultacja kończy się krótką notatką z wyborem i kompromisami.",
            [new("Przegląd architektury modułowego monolitu", "Oceniamy granice modułów, zależności i miejsca, w których warto zostawić monolit.", 60, 28000, 4, [0, 5]),
             new("Konsultacja nad jedną decyzją architektoniczną", "Jedno konkretne pytanie, na przykład kolejka czy wywołanie synchroniczne.", 30, 13000, 4, [5])],
            Stats(14, 3, 1, 2)),
        new(6, "Filip Syntetyczny", "demo-filip-syntetyczny", DemoStatus.Approved,
            "Code review i mentoring: czytelny kod, testy i zdrowy proces zespołu",
            "Prowadzę przeglądy kodu i warsztaty dla zespołów, które chcą pracować spokojniej. Skupiam się na testach, nazewnictwie i małych pull requestach. Przeglądam kod na żywo, tłumaczę powody uwag i zostawiam listę nawyków do wdrożenia w zespole.",
            [new("Code review na żywo", "Wspólnie przeglądamy wskazany kod, omawiam uwagi i priorytety poprawek.", 60, 22000, 5, [0, 1, 2]),
             new("Konsultacja o procesie przeglądów w zespole", "Rozmowa o tym, jak zorganizować przeglądy, żeby nie blokowały pracy.", 30, 11000, 5, [1])],
            Stats(60, 2, 0, 10)),
        new(7, "Grażyna Pokazowa", "demo-grazyna-pokazowa", DemoStatus.Submitted,
            "Wycena i plan projektu oprogramowania w oparciu o realne założenia",
            "Pomagam przygotować wycenę i plan projektu, który da się obronić przed zarządem. Rozbijam zakres na etapy, wskazuję niewiadome i proponuję sposób ich zmniejszenia. Konsultacja kończy się planem etapów z widełkami czasu i kosztu oraz listą pytań do klienta.",
            [new("Wycena projektu w widełkach", "Rozpisujemy zakres, niewiadome i widełki kosztu na podstawie Twojego opisu.", 60, 20000, 6, [0])],
            null),
        new(8, "Henryk Symulowski", "demo-henryk-symulowski", DemoStatus.Draft,
            "Szkic profilu: rozwiązywanie awarii produkcyjnych",
            "Profil w trakcie uzupełniania.",
            [], null),
        new(9, "Irena Makietowa", "demo-irena-makietowa", DemoStatus.Suspended,
            "Rozwiązywanie awarii produkcyjnych w aplikacjach webowych",
            "Pomagam zespołom, gdy produkcja przestaje działać: czytam logi, odtwarzam przebieg awarii i proponuję poprawkę oraz sposób, żeby się nie powtórzyła. Na konsultacji ustalamy, gdzie szukać przyczyny i jak zabezpieczyć system przed kolejnym incydentem.",
            [new("Analiza awarii produkcyjnej", "Razem przechodzimy przez logi i metryki z incydentu i szukamy przyczyny.", 60, 26000, 7, [0, 6])],
            Stats(9, 2, 0, 3),
            "Demo: konto zawieszone na czas wyjaśnienia skargi."),
        new(10, "Jan Szablonowski", "demo-jan-szablonowski", DemoStatus.Rejected,
            "Konsultacje z dowolnej technologii",
            "Robię wszystko, od stron po sztuczną inteligencję i sieci. Mam duże doświadczenie, ale trudno je tu opisać szczegółowo. Chętnie porozmawiam o Twoim projekcie i znajdziemy rozwiązanie, jakkolwiek będzie ono wyglądało w praktyce.",
            [new("Ogólna konsultacja techniczna", "Rozmowa o Twoim projekcie bez określonego zakresu.", 30, 9000, 0, [0])],
            null,
            "Demo: profil odrzucony, zbyt ogólny opis doświadczenia."),
    ];

    /// <summary>Declared after <see cref="Raw"/>: static fields initialise in text order.</summary>
    public static readonly DemoPractitioner[] Practitioners = WithSeededReviews(Raw);
}
