using Gabinety.Web.Api;

namespace Gabinety.Web.Tests;

/// <summary>Synthetic data (`demo-`): nothing here is a real person (AGENTS.md).</summary>
public static class Sample
{
    public static readonly TaxonomyDto Taxonomy = new("2026-11-v1",
        [new("audyt-kodu", "Audyt kodu"), new("migracja", "Migracja")],
        [new("dotnet", ".NET"), new("react", "React")]);

    public static PractitionerCardDto Card(string slug = "demo-anna-demowska", string name = "Anna Demowska", string headline = "Audyty kodu .NET i konsultacje architektury", string[]? badges = null,
        RatingDto? rating = null, bool isNew = true, long fromPrice = 25000, int[]? durations = null) =>
        new(Guid.NewGuid(), slug, name, headline, null, badges ?? ["email", "github", "approved"], rating, isNew, new MoneyDto(fromPrice, "PLN"), ["audyt-kodu"], durations ?? [30, 60]);

    public static SitemapDto Sitemap(int count = 6, bool indexable = true, string path = "/programisci") => new([new SitemapCategoryDto(path, count, indexable)]);

    public static BookingViewDto Booking(string title = "Konsultacja demo", string status = "Confirmed", string practitioner = "Anna Demowska", long price = 25000) =>
        new(Guid.NewGuid(), status, new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 1, 13, 0, 0, TimeSpan.Zero), 60, title,
            new MoneyDto(price, "PLN"), new PractitionerRefDto(Guid.NewGuid(), practitioner, "demo-anna-demowska"), new ClientRefDto("demo-client"));

    public static PageDto<PractitionerCardDto> Cards(int n = 2, string? next = null) =>
        new([.. Enumerable.Range(1, n).Select(i => Card($"demo-person-{i}", $"Osoba Demo {i}"))], next);
}
