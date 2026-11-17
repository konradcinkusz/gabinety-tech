using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Features.Internal.TestClock;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Internal.DemoReset;

/// <summary>
/// POST /internal/v1/demo/reset (CONTRACTS 10.4): removes the Reputation rows of the <c>demo-</c> namespace and returns the counts. Mapped only outside a
/// deployed environment. A review is in the namespace when its reviewer user id starts with <c>demo-</c> (case-sensitive); its moderation events go with it.
/// A rating summary is derived data keyed by the practitioner, not a row of the namespace: for each practitioner that lost reviews it is deleted when no
/// review of that practitioner is left, and otherwise recounted from the remaining Published reviews with its version raised (so it is pushed again).
/// Summaries of practitioners that lost nothing are not touched.
/// </summary>
public sealed class DemoResetFeature : IServiceFeature
{
    const string Ns = "demo-";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        if (!NonDeployedProfile.IsActive(endpoints.ServiceProvider.GetRequiredService<IConfiguration>())) return;
        endpoints.MapPost("/internal/v1/demo/reset", ResetAsync).RequireAuthorization("Internal");
    }

    static async Task<IResult> ResetAsync(ReputationDbContext db, CancellationToken ct)
    {
        // StartsWith translates to a case-sensitive prefix match on PostgreSQL (LIKE) and on the in-memory provider (ordinal).
        var reviews = await db.Reviews.Where(r => r.ClientUserId.StartsWith(Ns)).ToListAsync(ct);
        var ids = reviews.Select(r => r.Id).ToList();
        var events = await db.ModerationEvents.Where(e => ids.Contains(e.ReviewId)).ToListAsync(ct);
        var affected = reviews.Select(r => r.PractitionerId).Distinct().ToList();
        var summaries = await db.RatingSummaries.Where(s => affected.Contains(s.PractitionerId)).ToListAsync(ct);
        var remaining = await db.Reviews.Where(r => affected.Contains(r.PractitionerId) && !r.ClientUserId.StartsWith(Ns))
            .Select(r => new { r.PractitionerId, r.Status, r.Rating }).ToListAsync(ct);

        int deleted = 0, recomputed = 0;
        foreach (var s in summaries)
        {
            var left = remaining.Where(r => r.PractitionerId == s.PractitionerId).ToList();
            if (left.Count == 0) { db.RatingSummaries.Remove(s); deleted++; continue; }
            var published = left.Where(r => r.Status == ReviewStatus.Published).ToList();
            var (count, sum) = (published.Count, published.Sum(r => (int)r.Rating));
            if (s.Count == count && s.Sum == sum) continue;
            s.Count = count; s.Sum = sum; s.Version++;
            recomputed++;
        }
        db.RemoveRange(events);
        db.RemoveRange(reviews);
        await db.SaveChangesAsync(ct); // one unit of work: all or nothing

        return Results.Ok(new { reviews = reviews.Count, moderationEvents = events.Count, ratingSummariesDeleted = deleted, ratingSummariesRecomputed = recomputed });
    }
}
