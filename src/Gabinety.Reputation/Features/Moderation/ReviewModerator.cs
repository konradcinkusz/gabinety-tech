using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Features.Reviews.Summary;
using Gabinety.Reputation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Moderation;

public enum ModerationAction { Publish, Reject }

public enum ModerationOutcome { Done, NotFound, InvalidTransition }

/// <summary>
/// The review state machine of CONTRACTS 8.3 as an administrator drives it: publish only from <c>Held</c>; reject from <c>Held</c> or <c>Published</c>;
/// <c>Rejected</c> is final. A decision, its <c>moderation_events</c> row and the change to the rating summary are one transaction, so the summary
/// always equals a recount of the Published rows: publishing adds the review to count and sum, rejecting a Published review removes it, and either way
/// version increments once. Rejecting a Held review never counted, so it leaves the summary (and its version) alone.
/// A repeated or losing decision is <see cref="ModerationOutcome.InvalidTransition"/> and writes nothing. <paramref name="note"/> is moderator free text:
/// it is stored in the row and the event and is never logged.
/// </summary>
public static class ReviewModerator
{
    public static bool Allowed(ReviewStatus from, ModerationAction action) =>
        action == ModerationAction.Publish ? from == ReviewStatus.Held : from is ReviewStatus.Held or ReviewStatus.Published;

    public static async Task<(ModerationOutcome Outcome, Guid PractitionerId, ReviewStatus Status)> ApplyAsync(ReputationDbContext db, Guid reviewId,
        ModerationAction action, string actor, string? note, DateTimeOffset now, CancellationToken ct)
    {
        var target = action == ModerationAction.Publish ? ReviewStatus.Published : ReviewStatus.Rejected;
        if (!db.Database.IsRelational())
        {
            // InMemory has no row locks or ExecuteUpdate: serialise on the same gate the create path uses.
            await RatingSummaryStore.InMemoryGate.WaitAsync(ct);
            try
            {
                var review = await db.Reviews.SingleOrDefaultAsync(x => x.Id == reviewId, ct);
                if (review is null) return (ModerationOutcome.NotFound, default, default);
                if (!Allowed(review.Status, action)) return (ModerationOutcome.InvalidTransition, review.PractitionerId, review.Status);
                var from = review.Status;
                review.Status = target; review.ModeratedBy = actor; review.ModerationNote = note;
                if (action == ModerationAction.Publish) review.PublishedAt = now;
                db.ModerationEvents.Add(Event(reviewId, action, actor, note, now));
                await db.SaveChangesAsync(ct);
                await ApplySummaryAsync(db, review.PractitionerId, review.Rating, from, target, ct);
                return (ModerationOutcome.Done, review.PractitionerId, target);
            }
            finally { RatingSummaryStore.InMemoryGate.Release(); }
        }

        var result = (Outcome: ModerationOutcome.NotFound, PractitionerId: Guid.Empty, Status: default(ReviewStatus));
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from the database
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var row = await db.Reviews.AsNoTracking().Where(x => x.Id == reviewId)
                .Select(x => new { x.PractitionerId, x.Rating, x.Status }).SingleOrDefaultAsync(ct);
            if (row is null) { result = (ModerationOutcome.NotFound, default, default); return; }
            if (!Allowed(row.Status, action)) { result = (ModerationOutcome.InvalidTransition, row.PractitionerId, row.Status); return; }

            // One conditional UPDATE decides a race: the loser waits for the winner's commit, then matches no row.
            var from = row.Status;
            var changed = action == ModerationAction.Publish
                ? await db.Reviews.Where(x => x.Id == reviewId && x.Status == from).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, target).SetProperty(x => x.ModeratedBy, actor).SetProperty(x => x.ModerationNote, note).SetProperty(x => x.PublishedAt, (DateTimeOffset?)now), ct)
                : await db.Reviews.Where(x => x.Id == reviewId && x.Status == from).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, target).SetProperty(x => x.ModeratedBy, actor).SetProperty(x => x.ModerationNote, note), ct);
            if (changed == 0) { result = (ModerationOutcome.InvalidTransition, row.PractitionerId, row.Status); return; }

            db.ModerationEvents.Add(Event(reviewId, action, actor, note, now));
            await db.SaveChangesAsync(ct);
            await ApplySummaryAsync(db, row.PractitionerId, row.Rating, from, target, ct);
            await tx.CommitAsync(ct);
            result = (ModerationOutcome.Done, row.PractitionerId, target);
        });
        return result;
    }

    static Task ApplySummaryAsync(ReputationDbContext db, Guid practitionerId, int rating, ReviewStatus from, ReviewStatus to, CancellationToken ct)
    {
        if (to == ReviewStatus.Published) return RatingSummaryStore.ApplyAsync(db, practitionerId, 1, rating, ct);
        if (from == ReviewStatus.Published) return RatingSummaryStore.ApplyAsync(db, practitionerId, -1, -rating, ct);
        return Task.CompletedTask;
    }

    static ModerationEvent Event(Guid reviewId, ModerationAction action, string actor, string? note, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(), ReviewId = reviewId, At = at, Actor = actor, Note = note,
        Kind = action == ModerationAction.Publish ? ModerationEventKind.Published : ModerationEventKind.Rejected,
    };
}
