using System.Security.Claims;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Features.Reviews.Summary;
using Gabinety.Reputation.Persistence;
using Gabinety.Reputation.Rules;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Reviews.Reply;

/// <summary>
/// POST /api/v1/reviews/{id}/reply (CONTRACTS 5.2, SPEC 6.3, D-29): the practitioner a review is ABOUT replies once, and only to a <c>Published</c> review.
/// The reply is user free text: it is stored in the review row only and never reaches a log, an exception message, a metric, a trace or an error body.
/// Order: input shape (400), the review rules except the review's own length floor (422 <c>contact_details_not_allowed</c> for a link or contact detail,
/// <c>invalid_input</c> for a blocked term or repetition; refused, never held), the caller's practitioner id from Directory (503 on outage, 404 for a user who is no practitioner), the review (404 when it is
/// unknown, someone else's or not Published, so nothing about other reviews is revealed), then one reply per review (409 <c>already_replied</c>).
/// The write is one conditional UPDATE (no reply yet, still Published, still this practitioner's) with its <c>Replied</c> event in one transaction, so two
/// concurrent replies produce exactly one 201 and one 409, and a reply that loses to a rejection is a 404. The rating summary is not touched.
/// </summary>
public sealed class ReplyReviewFeature : IServiceFeature
{
    public const int MinLength = 10, MaxLength = 1000;
    const string TooShortAfterTrim = "too_short_after_trim";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/reviews/{id:guid}/reply", HandleAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> HandleAsync(Guid id, ReplyRequest? req, ClaimsPrincipal user, ReputationDbContext db, IDirectoryClient directory,
        IEnumerable<IReviewRule> rules, TimeProvider time, ILogger<ReplyReviewFeature> log, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        if (req is null) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { ["body"] = ["The body must be a valid JSON object"] });
        var text = req.Text?.Trim() ?? "";
        if (text.Length is < MinLength or > MaxLength)
            return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { ["text"] = [$"text must be {MinLength} to {MaxLength} characters"] }); // never the text

        // A reply is public text: the review rules that are about leakage and abuse apply to it (SPEC 9). It is refused, never held: the practitioner is told and can retry.
        // too_short_after_trim is the review's 20 character floor and does not apply (the reply's own floor is 10, checked above).
        var tripped = rules.Where(r => r.Code != TooShortAfterTrim).Select(r => r.Check(ReviewText.From(text))).OfType<string>().ToList();
        if (tripped.Count > 0)
        {
            var leak = tripped.Any(c => c is "contains_link" or "contact_details");
            return Problem(leak ? ErrorCodes.ContactDetailsNotAllowed : ErrorCodes.InvalidInput, leak ? "Contact details not allowed" : "Invalid input", 422,
                new() { ["text"] = [.. tripped] }); // reason codes only, never the text
        }

        Guid practitionerId;
        try
        {
            if (await directory.GetPractitionerByUserAsync(sub, ct) is not { } found) return Problem(ErrorCodes.NotFound, "Not found", 404);
            practitionerId = found.PractitionerId;
        }
        catch (DirectoryUnavailableException)
        {
            return Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, null, "A required service is temporarily unavailable.");
        }

        var now = time.GetUtcNow();
        var outcome = await ApplyAsync(db, id, practitionerId, sub, text, now, ct);
        switch (outcome)
        {
            case ReplyOutcome.NotFound: return Problem(ErrorCodes.NotFound, "Not found", 404);
            case ReplyOutcome.AlreadyReplied: return Problem(ErrorCodes.AlreadyReplied, "Already replied", 409);
        }
        log.LogInformation("Review {ReviewId} of practitioner {PractitionerId} replied to", id, practitionerId); // ids only
        return Results.Json(new ReplyDto(text, now), statusCode: 201);
    }

    enum ReplyOutcome { Done, NotFound, AlreadyReplied }

    static async Task<ReplyOutcome> ApplyAsync(ReputationDbContext db, Guid id, Guid practitionerId, string actor, string text, DateTimeOffset now, CancellationToken ct)
    {
        var @event = new ModerationEvent { Id = Guid.CreateVersion7(), ReviewId = id, Kind = ModerationEventKind.Replied, Actor = actor, Note = null, At = now };

        if (!db.Database.IsRelational())
        {
            // InMemory has no row locks or ExecuteUpdate: serialise on the same gate the create and moderation paths use.
            await RatingSummaryStore.InMemoryGate.WaitAsync(ct);
            try
            {
                var review = await db.Reviews.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (review is null || review.PractitionerId != practitionerId || review.Status != ReviewStatus.Published) return ReplyOutcome.NotFound;
                if (review.ReplyText is not null) return ReplyOutcome.AlreadyReplied;
                review.ReplyText = text; review.RepliedAt = now;
                db.ModerationEvents.Add(@event);
                await db.SaveChangesAsync(ct);
                return ReplyOutcome.Done;
            }
            finally { RatingSummaryStore.InMemoryGate.Release(); }
        }

        var outcome = ReplyOutcome.Done;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from the database, not from a half-applied attempt
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // The WHERE clause is the decision: a concurrent reply (or rejection) that committed first makes this update match no row.
            var changed = await db.Reviews
                .Where(x => x.Id == id && x.PractitionerId == practitionerId && x.Status == ReviewStatus.Published && x.ReplyText == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplyText, text).SetProperty(x => x.RepliedAt, now), ct);
            if (changed == 0)
            {
                var current = await db.Reviews.AsNoTracking().Where(x => x.Id == id && x.PractitionerId == practitionerId)
                    .Select(x => new { x.ReplyText }).SingleOrDefaultAsync(ct);
                outcome = current?.ReplyText is not null ? ReplyOutcome.AlreadyReplied : ReplyOutcome.NotFound;
                return;
            }
            db.ModerationEvents.Add(@event);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            outcome = ReplyOutcome.Done;
        });
        return outcome;
    }

    static IResult Problem(string code, string title, int status, Dictionary<string, string[]>? errors = null, string? detail = null) =>
        Results.Json(ApiProblem.Of(code, title, status, detail, errors), statusCode: status, contentType: "application/problem+json");
}
