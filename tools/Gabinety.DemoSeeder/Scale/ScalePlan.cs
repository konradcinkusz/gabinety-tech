namespace Gabinety.DemoSeeder;

/// <summary>One synthetic practitioner of the scaled dataset (T-10.04). Everything is derived from the number, so two runs plan the same data.</summary>
public sealed record ScaledPractitioner(int Number, string DisplayName, string Slug, string Headline, string Bio, DemoOffering[] Offerings, DemoStats Stats)
{
    public string UserName => $"demo-practitioner-{Number}";
    public string GithubLogin => $"demo-dev-{Number}";
    public string PortfolioUrl => $"https://demo-{Number}.portfolio.example.com/";
    public string MeetingUrl => $"https://meet.jit.si/demo-gabinety-{Number}";
    public string Nip => Nips.FirstValidFrom(999_000_000 + Number * 100);
}

/// <summary>
/// The scaled dataset behind <c>--factor N</c> (SPEC 13: 2000 practitioners and 20000 reviews at factor 200). Pure: no HTTP, so the counts are testable.
/// Factor 1 is the ordinary demo (nothing is added). Factor F means <c>10 * F</c> practitioners in all (the ten of <see cref="DemoCatalog"/> plus <c>10 * F - 10</c> Approved ones numbered from 11)
/// and <c>100 * F</c> reviews in all, counted as the rating summary the Directory ranks and lists by (the sum of <see cref="DemoStats.Reviews"/> over the catalog and the extras).
/// The reviews are the Directory's rating counts, not review rows in Reputation: a review row needs a Completed booking, which the seeder cannot make by the thousand.
/// </summary>
public static class ScalePlan
{
    public const int PractitionersPerFactor = 10, ReviewsPerFactor = 100, FirstExtraNumber = 11;

    public static int TotalPractitioners(int factor) => PractitionersPerFactor * factor;
    public static int TotalReviews(int factor) => ReviewsPerFactor * factor;
    public static int ExtraCount(int factor) => Math.Max(0, TotalPractitioners(factor) - DemoCatalog.Practitioners.Length);

    static readonly string[] FirstNames = ["Adam", "Beata", "Cezary", "Dorota", "Emil", "Felicja", "Gustaw", "Halina", "Igor", "Jolanta", "Kamil", "Lucyna", "Marek", "Natalia", "Oskar", "Paulina"];
    static readonly string[] LastNames = ["Demowski", "Testowa", "Probny", "Fikcyjna", "Wzorowy", "Syntetyczna", "Pokazowy", "Makietowa", "Przykladowy", "Szablonowa", "Symulowany", "Zastepcza"];
    static readonly string[] Topics =
    [
        "audyty kodu .NET", "migracje z .NET Framework", "integracje modeli jezykowych", "automatyzacja procesow w malej firmie",
        "architektura systemow", "code review i mentoring", "wycena i plan projektu", "analiza awarii produkcyjnych",
    ];

    public static IReadOnlyList<ScaledPractitioner> Extras(int factor)
    {
        if (factor < 1) throw new ArgumentOutOfRangeException(nameof(factor), "the factor is at least 1");
        var count = ExtraCount(factor);
        var baseReviews = DemoCatalog.Practitioners.Where(p => p.Stats is not null).Sum(p => p.Stats!.Reviews);
        var remaining = Math.Max(0, TotalReviews(factor) - baseReviews);
        var (per, rest) = count == 0 ? (0, 0) : (remaining / count, remaining % count); // the first `rest` extras carry one more, so the total is exact
        var list = new List<ScaledPractitioner>(count);
        for (var i = 0; i < count; i++)
        {
            var n = FirstExtraNumber + i;
            var topic = Topics[i % Topics.Length];
            var reviews = per + (i < rest ? 1 : 0);
            var average = reviews == 0 ? 0m : 3.5m + (i * 7 % 16) / 10m; // 3.5 to 5.0, deterministic
            var stats = new DemoStats(reviews, average, Completed: reviews + i % 5, Cancellations: i % 4 == 0 ? 1 : 0, NoShows: i % 11 == 0 ? 1 : 0, OpenSlots: i % 11);
            var title = $"Konsultacja: {topic}";
            list.Add(new ScaledPractitioner(n, $"{FirstNames[i % FirstNames.Length]} {LastNames[i / FirstNames.Length % LastNames.Length]}", $"demo-scale-{n}",
                $"Konsultacje z zakresu: {topic}",
                $"Syntetyczny profil do pomiarow wydajnosci. Pomagam zespolom w tematach: {topic}. Na konsultacji omawiamy konkretny przypadek, ryzyka i kolejnosc dzialan, a na koncu dostajesz krotka liste krokow do wdrozenia.",
                i % 2 == 0
                    ? [new DemoOffering(title, $"Wspolnie przechodzimy przez Twoj przypadek: {topic}.", 60, 15000 + i % 20 * 1000, i % 8, [i % 8, (i + 3) % 8])]
                    : [new DemoOffering(title, $"Wspolnie przechodzimy przez Twoj przypadek: {topic}.", 60, 15000 + i % 20 * 1000, i % 8, [i % 8]),
                       new DemoOffering($"Krotka {title.ToLowerInvariant()}", $"Krotka rozmowa o jednym pytaniu: {topic}.", 30, 8000 + i % 10 * 1000, (i + 1) % 8, [(i + 2) % 8])],
                stats));
        }
        return list;
    }
}
