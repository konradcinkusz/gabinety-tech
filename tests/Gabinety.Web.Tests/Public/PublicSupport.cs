using System.Net;
using Gabinety.Web.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Web.Tests;

/// <summary>Synthetic public data (`demo-`) and a host whose backends answer the public reads of the wave-2 pages.</summary>
public static class PublicSupport
{
    public static readonly Guid PractitionerId = Guid.Parse("018f0000-0000-7000-8000-0000000000aa");
    public static readonly Guid OfferA = Guid.Parse("018f0000-0000-7000-8000-0000000000b1");
    public static readonly Guid OfferB = Guid.Parse("018f0000-0000-7000-8000-0000000000b2");
    /// <summary>The fixed clock of the page tests: Monday 2026-11-23 10:00 UTC (11:00 in Warsaw).</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);

    public static PractitionerProfileDto Profile(string headline = "Audyty kodu .NET", string? portfolio = "https://demo.example/portfolio", string? github = "demo-anna", bool isNew = true,
        RatingDto? rating = null, IReadOnlyList<OfferingDto>? offerings = null, string bio = "Pracuję z systemami demo od lat.") =>
        new(PractitionerId, "demo-anna-demowska", "Anna Ąćęł Demowska", headline, github, ["email", "github", "nip", "approved"], rating, isNew, new MoneyDto(18000, "PLN"), bio, portfolio,
            offerings ?? [new(OfferA, "Audyt kodu .NET", "Przegląd repozytorium demo", 60, new MoneyDto(18000, "PLN"), "audyt-kodu", ["dotnet"]),
                          new(OfferB, "Krótka konsultacja", "Pytania o architekturę", 30, new MoneyDto(11000, "PLN"), "audyt-kodu", ["dotnet"])],
            4, new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));

    public static RankingRulesDto Rules => new("2026-11-v1", new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero), new RankingWeightsDto(0.45, 0.25, 0.15, 0.15), new RankingConstantsDto(5, 3.8, 3, 10));

    public static SlotDto Slot(int day, int hourUtc, int month = 11) => new(new DateTimeOffset(2026, month, day, hourUtc, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, month, day, hourUtc + 1, 0, 0, TimeSpan.Zero));

    public static WebFactory Host(Func<HttpRequestMessage, HttpResponseMessage>? directory = null, Func<HttpRequestMessage, HttpResponseMessage>? booking = null, Func<HttpRequestMessage, HttpResponseMessage>? reputation = null)
    {
        var web = new WebFactory().Configure(s => s.AddSingleton<TimeProvider>(new FixedTime(Now)));
        web.Directory.Respond = directory ?? DefaultDirectory();
        web.Booking.Respond = booking ?? (_ => FakeBackend.Json(new SlotsDto([Slot(24, 9), Slot(24, 10), Slot(25, 13)])));
        web.Reputation.Respond = reputation ?? (_ => FakeBackend.Json(new PageDto<ReviewDto>([], null)));
        return web;
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> DefaultDirectory(PractitionerProfileDto? profile = null) => r => r.RequestUri!.AbsolutePath switch
    {
        "/api/v1/taxonomy" => FakeBackend.Json(Sample.Taxonomy),
        "/api/v1/sitemap" => FakeBackend.Json(new SitemapDto([new("/programisci", 6, true), new("/programisci/audyt-kodu", 6, true), new("/programisci/migracja", 2, false)],
            [new("demo-anna-demowska", new DateTimeOffset(2026, 11, 20, 8, 30, 0, TimeSpan.Zero)), new("demo-bartek", new DateTimeOffset(2026, 11, 21, 8, 30, 0, TimeSpan.Zero))])),
        "/api/v1/ranking-rules" => FakeBackend.Json(Rules),
        "/api/v1/practitioners/demo-anna-demowska" => FakeBackend.Json(profile ?? Profile()),
        "/api/v1/practitioners" => FakeBackend.Json(Sample.Cards(2)),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };
}

/// <summary>A clock that stands still.</summary>
public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
