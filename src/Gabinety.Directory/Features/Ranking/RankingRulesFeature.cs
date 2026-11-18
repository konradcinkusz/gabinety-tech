using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Search;
using Gabinety.Ranking;
using Gabinety.ServiceDefaults;
using System.Globalization;

namespace Gabinety.Directory.Features.Ranking;

/// <summary>GET /api/v1/ranking-rules: version, weights and constants straight from <see cref="RankingRules"/>; only the effective date is configuration (Ranking:EffectiveFrom).</summary>
public sealed class RankingRulesFeature : IServiceFeature
{
    const string DefaultEffectiveFrom = "2026-11-16T00:00:00Z";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/ranking-rules", (HttpContext ctx, IConfiguration config) =>
        {
            var raw = config["Ranking:EffectiveFrom"] ?? DefaultEffectiveFrom;
            var from = DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
                ? d.ToUniversalTime() : DateTimeOffset.Parse(DefaultEffectiveFrom, CultureInfo.InvariantCulture);
            return PublicJson.Respond(ctx, new RankingRulesDto(RankingRules.Version, from,
                new RankingWeights(RankingRules.WeightRating, RankingRules.WeightReliability, RankingRules.WeightAvailability, RankingRules.WeightCompleteness),
                new RankingConstants(RankingRules.M, RankingRules.Mu, RankingRules.MinReviewsToShowRating, RankingRules.SlotsForFullAvailability)),
                "public, max-age=3600");
        }).AllowAnonymous();
}
