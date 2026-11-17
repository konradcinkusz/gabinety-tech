using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Internal.Erasure;

/// <summary>
/// DELETE /internal/v1/subjects/{userId} (CONTRACTS 5.2, SPEC 5 rule 4), behind the "Internal" policy. For every review the user wrote it replaces the text, the
/// practitioner's reply and the moderator's note (all can quote the reviewer) with <see cref="ErasedText"/> and replaces the user id with a random value, and
/// it clears the note of Published, Rejected and Replied events. It KEEPS the rating, the status and <c>rating_summaries</c>: the rating still counts (accounting).
/// Idempotent: a second call finds no review of the old id. An unknown user is 204, as Directory's erasure answers. Nothing here logs a text or the user id.
/// A demo user stays in the <c>demo-</c> namespace so the demo reset still finds the rows.
/// </summary>
public sealed class ErasureFeature : IServiceFeature
{
    public const string ErasedText = "Opinia usunięta na wniosek autora";
    const string DemoPrefix = "demo-";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapDelete("/internal/v1/subjects/{userId}", EraseAsync).RequireAuthorization("Internal");

    static async Task<IResult> EraseAsync(string userId, ReputationDbContext db, ILogger<ErasureFeature> log, CancellationToken ct)
    {
        var reviews = await db.Reviews.Where(r => r.ClientUserId == userId).ToListAsync(ct);
        if (reviews.Count == 0) return Results.NoContent();

        var ids = reviews.Select(r => r.Id).ToList();
        var events = await db.ModerationEvents.Where(e => ids.Contains(e.ReviewId) && e.Note != null && e.Kind != ModerationEventKind.Held && e.Kind != ModerationEventKind.Created).ToListAsync(ct);
        var prefix = userId.StartsWith(DemoPrefix, StringComparison.Ordinal) ? "demo-erased-" : "erased-";
        foreach (var r in reviews)
        {
            r.Text = ErasedText;
            if (r.ReplyText is not null) r.ReplyText = ErasedText;
            if (r.ModerationNote is not null) r.ModerationNote = ErasedText;
            r.ClientUserId = prefix + Guid.NewGuid().ToString("N");
        }
        foreach (var e in events) e.Note = ErasedText;
        await db.SaveChangesAsync(ct); // one unit of work: all or nothing

        log.LogInformation("Erased {Count} reviews of one subject", reviews.Count);
        return Results.NoContent();
    }
}
