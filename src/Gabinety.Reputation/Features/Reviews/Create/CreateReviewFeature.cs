using System.Security.Claims;
using Gabinety.Contracts.Assistant;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Clients;
using Gabinety.Reputation.Jobs.SummaryPush;
using Gabinety.Reputation.Features.Reviews.Summary;
using Gabinety.Reputation.Persistence;
using Gabinety.Reputation.Rules;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gabinety.Reputation.Features.Reviews.Create;

/// <summary>
/// POST /api/v1/reviews (CONTRACTS 5.2, 5.3 steps 1 and 3). The review text is user free text: it is stored in the review row only and never reaches
/// a log, an exception message, a metric, a trace or any response. A review that trips a rule is created Held and never reaches the Assistant;
/// one that passes goes to the Assistant triage (5.3 step 2, 25 s, no retry) BEFORE the write transaction opens, so no transaction is held across
/// that call: <c>hold</c> creates it Held with the answer's reasons, <c>none</c> Published, any failure Published with <c>triage_flag = unavailable</c>.
/// Order: input shape, Booking's answer (404 when the booking is unknown or not the caller's, so a booking id reveals nothing), one review per
/// booking (409), then the 14 day / completed rule (422).
/// </summary>
public sealed class CreateReviewFeature : IServiceFeature
{
    const string UniqueViolation = "23505";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/reviews", HandleAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> HandleAsync(CreateReviewRequest? req, ClaimsPrincipal user, ReputationDbContext db, IBookingClient booking,
        IAssistantClient assistant, PublicationDecider decider, SummaryPushSignal pushSignal, TimeProvider time, ILogger<CreateReviewFeature> log, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        var errors = new Dictionary<string, string[]>();
        if (req is null) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { ["body"] = ["The body must be a valid JSON object"] });
        if (req.BookingId == Guid.Empty) errors["bookingId"] = ["bookingId is required"];
        if (req.Rating is < 1 or > 5) errors["rating"] = ["rating must be 1 to 5"];
        var decision = decider.Decide(req.Text);
        if (decision is PublicationDecision.Invalid invalid) errors["text"] = [.. invalid.Errors]; // codes only, never the text
        if (errors.Count > 0) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, errors);

        ReviewabilityDtoView? view;
        try { view = await booking.GetReviewabilityAsync(req.BookingId, ct) is { } r ? new(r.Reviewable, r.ClientUserId, r.PractitionerId) : null; }
        catch (BookingUnavailableException)
        {
            return Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, null, "A required service is temporarily unavailable.");
        }
        if (view is null || view.ClientUserId != sub) return Problem(ErrorCodes.NotFound, "Not found", 404);
        if (await db.Reviews.AsNoTracking().AnyAsync(x => x.BookingId == req.BookingId, ct))
            return Problem(ErrorCodes.AlreadyReviewed, "Already reviewed", 409);
        if (!view.Reviewable) return Problem(ErrorCodes.NotReviewable, "Not reviewable", 422);

        // 5.3 step 2: only a review that passed the rules is asked, and the call is made before any transaction opens.
        var held = decision as PublicationDecision.Held;
        var text = req.Text.Trim();
        var triage = held is null ? await TriageAsync(assistant, req.Rating, text, log, ct) : null;
        var triageHold = triage as AssistantTriageResult.Verdict is { Flag: TriageVerdict.Hold } hv ? hv : null;

        var now = time.GetUtcNow();
        var status = held is null && triageHold is null ? ReviewStatus.Published : ReviewStatus.Held;
        var review = new Review
        {
            Id = Guid.CreateVersion7(), BookingId = req.BookingId, PractitionerId = view.PractitionerId, ClientUserId = sub,
            Rating = (short)req.Rating, Text = text, Status = status,
            HoldReasons = held is not null ? [.. held.Reasons] : triageHold is not null ? [.. triageHold.Reasons.Select(TriageWire.Code)] : [],
            TriageFlag = triage switch
            {
                AssistantTriageResult.Verdict { Flag: TriageVerdict.Hold } => TriageFlag.Hold,
                AssistantTriageResult.Verdict => TriageFlag.None,
                null => TriageFlag.None, // a rule held it: the Assistant was never asked
                _ => TriageFlag.Unavailable,
            },
            TriageModelId = (triage as AssistantTriageResult.Verdict)?.ModelId,
            CreatedAt = now, PublishedAt = status == ReviewStatus.Published ? now : null,
        };
        var events = new List<ModerationEvent> { Event(review.Id, ModerationEventKind.Created, null, now) };
        events.Add(status == ReviewStatus.Published
            ? Event(review.Id, ModerationEventKind.Published, null, now)
            : Event(review.Id, ModerationEventKind.Held, string.Join(',', review.HoldReasons), now, triageHold is null ? "system" : "triage")); // reason codes, fixed vocabulary

        try
        {
            if (!await SaveAsync(db, review, events, ct)) return Problem(ErrorCodes.AlreadyReviewed, "Already reviewed", 409);
        }
        catch (DbUpdateException e) when (IsUnique(e))
        {
            db.ChangeTracker.Clear();
            return Problem(ErrorCodes.AlreadyReviewed, "Already reviewed", 409); // the unique index on booking_id decides a race
        }

        if (status == ReviewStatus.Published) pushSignal.Notify(); // the summary changed and is committed: push it now, not at the next tick
        log.LogInformation("Review {ReviewId} created for practitioner {PractitionerId} as {Status} (triage {TriageFlag})", review.Id, review.PractitionerId, status, review.TriageFlag);
        return Results.Json(new CreateReviewResponse(review.Id, status), statusCode: 201);
    }

    /// <summary>Review, its events and the summary change in one transaction. False only on the non-relational path when a concurrent review won.</summary>
    static async Task<bool> SaveAsync(ReputationDbContext db, Review review, List<ModerationEvent> events, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            await RatingSummaryStore.InMemoryGate.WaitAsync(ct);
            try
            {
                if (await db.Reviews.AnyAsync(x => x.BookingId == review.BookingId, ct)) return false;
                db.Reviews.Add(review); db.ModerationEvents.AddRange(events);
                await db.SaveChangesAsync(ct);
                if (review.Status == ReviewStatus.Published) await RatingSummaryStore.ApplyAsync(db, review.PractitionerId, 1, review.Rating, ct);
                return true;
            }
            finally { RatingSummaryStore.InMemoryGate.Release(); }
        }
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // a retry starts from the database, not from a half-applied attempt
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Reviews.Add(review); db.ModerationEvents.AddRange(events);
            await db.SaveChangesAsync(ct);
            if (review.Status == ReviewStatus.Published) await RatingSummaryStore.ApplyAsync(db, review.PractitionerId, 1, review.Rating, ct);
            await tx.CommitAsync(ct);
        });
        return true;
    }

    /// <summary>The Assistant is advice: any failure, even an exception from the client, is "unavailable" and never blocks the review. The text is never logged.</summary>
    static async Task<AssistantTriageResult> TriageAsync(IAssistantClient assistant, int rating, string text, ILogger log, CancellationToken ct)
    {
        try { return await assistant.TriageAsync(rating, text, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogWarning("Triage call failed with {ExceptionType}", ex.GetType().Name); // the message may carry the text, the URL or the key
            return new AssistantUnavailable("transport");
        }
    }

    static ModerationEvent Event(Guid reviewId, ModerationEventKind kind, string? note, DateTimeOffset at, string actor = "system") =>
        new() { Id = Guid.CreateVersion7(), ReviewId = reviewId, Kind = kind, Actor = actor, Note = note, At = at };

    static bool IsUnique(Exception e)
    {
        for (var x = e; x is not null; x = x.InnerException) if (x is PostgresException { SqlState: UniqueViolation }) return true;
        return false;
    }

    sealed record ReviewabilityDtoView(bool Reviewable, string ClientUserId, Guid PractitionerId);

    static IResult Problem(string code, string title, int status, Dictionary<string, string[]>? errors = null, string? detail = null) =>
        Results.Json(ApiProblem.Of(code, title, status, detail, errors), statusCode: status, contentType: "application/problem+json");
}
