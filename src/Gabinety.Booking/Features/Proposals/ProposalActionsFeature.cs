using System.Security.Claims;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// POST /api/v1/proposals/{id}/accept | counter | reject | withdraw (ADR proposal 0013 section 3). Order of answers: not a party 404; a closed proposal, the wrong turn, a stale round number, a lost
/// race or the counter cap 409 <c>invalid_transition</c>; then, for a counter, invalid terms 422 and contact details 422. The round number the caller saw last is in the body (accept, counter,
/// reject) and is checked against the proposal, so an answer to terms that have since changed is refused; withdraw needs none. The round count is the row's concurrency token.
/// </summary>
public sealed class ProposalActionsFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1/proposals/{id:guid}").RequireAuthorization("Authenticated");
        g.MapPost("accept", (Guid id, HttpContext http, ClaimsPrincipal user, BookingDbContext db, ProposalViewBuilder views, INotificationWriter n, TimeProvider t, IConfiguration c, ILogger<ProposalActionsFeature> l, CancellationToken ct) =>
            ActAsync(id, ProposalTrigger.Accept, http, user, db, views, n, t, c, l, ct));
        g.MapPost("counter", (Guid id, HttpContext http, ClaimsPrincipal user, BookingDbContext db, ProposalViewBuilder views, INotificationWriter n, TimeProvider t, IConfiguration c, ILogger<ProposalActionsFeature> l, CancellationToken ct) =>
            ActAsync(id, ProposalTrigger.Counter, http, user, db, views, n, t, c, l, ct));
        g.MapPost("reject", (Guid id, HttpContext http, ClaimsPrincipal user, BookingDbContext db, ProposalViewBuilder views, INotificationWriter n, TimeProvider t, IConfiguration c, ILogger<ProposalActionsFeature> l, CancellationToken ct) =>
            ActAsync(id, ProposalTrigger.Reject, http, user, db, views, n, t, c, l, ct));
        g.MapPost("withdraw", (Guid id, HttpContext http, ClaimsPrincipal user, BookingDbContext db, ProposalViewBuilder views, INotificationWriter n, TimeProvider t, IConfiguration c, ILogger<ProposalActionsFeature> l, CancellationToken ct) =>
            ActAsync(id, ProposalTrigger.Withdraw, http, user, db, views, n, t, c, l, ct));
    }

    static IResult Invalid() => CreateProposalFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);

    static async Task<IResult> ActAsync(Guid id, ProposalTrigger trigger, HttpContext http, ClaimsPrincipal user, BookingDbContext db, ProposalViewBuilder views,
        INotificationWriter notifications, TimeProvider time, IConfiguration config, ILogger<ProposalActionsFeature> log, CancellationToken ct)
    {
        var sub = CreateProposalFeature.Sub(user);
        if (sub is null) return Results.Unauthorized();

        // PostgreSQL decides a race by the round count (the concurrency token), the unique (proposal, round) key and the rollback of the single save. The in-memory provider has none of
        // the three, so it gets a process lock per proposal, as the booking create has per practitioner (the key is a Guid: a proposal id never equals a practitioner id).
        using var held = db.Database.IsRelational() ? null : await PractitionerLocks.AcquireAsync(id, ct);
        var p = await db.Proposals.Include(x => x.Rounds).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p is null || ProposalViewBuilder.RoleOf(p, sub) is not { } role) return CreateProposalFeature.Problem(ErrorCodes.NotFound, "Not found", 404);

        // the body: a round number, and for a counter the new terms and an optional note
        int? seenRound = null; ProposalTerms? terms = null; string? rawText = null;
        if (trigger == ProposalTrigger.Counter)
        {
            var body = await CreateProposalFeature.ReadAsync<CounterProposalRequest>(http.Request, ct);
            if (body is null) return CreateProposalFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { ["body"] = ["a JSON body of a counter-proposal"] });
            (seenRound, terms, rawText) = (body.Round, body.Terms, body.Text);
        }
        else if (trigger != ProposalTrigger.Withdraw)
        {
            var body = await CreateProposalFeature.ReadAsync<ProposalActionRequest>(http.Request, ct);
            if (body is null) return CreateProposalFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { ["round"] = ["the round you answer"] });
            seenRound = body.Round;
        }

        // 409: the proposal must be open, it must be this party's turn, and the round must be the one the caller saw
        if (ProposalStateMachine.Next(p.Status, trigger, role) is not { } next) return Invalid();
        if (seenRound is { } r && r != p.RoundCount) return Invalid();
        if (trigger == ProposalTrigger.Counter && p.RoundCount > ProposalStateMachine.MaxCounters) return Invalid();

        // 422: the terms and the note of a counter
        string? text = null;
        if (trigger == ProposalTrigger.Counter)
        {
            var now0 = time.GetUtcNow();
            var errors = ProposalTermsRules.Validate(p.Kind, terms, CreateProposalFeature.TodayWarsaw(now0));
            text = CreateProposalFeature.CleanText(rawText, required: false, 1000, "text", errors);
            if (errors.Count > 0) return CreateProposalFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, errors);
            if (text is not null && ProposalTextRules.HasContactDetails(text))
                return CreateProposalFeature.Problem(ErrorCodes.ContactDetailsNotAllowed, "Contact details are not allowed in a proposal", 422);
            terms = ProposalTermsRules.Normalize(p.Kind, terms!);
        }

        var now = time.GetUtcNow();
        var previous = p.RoundCount;
        p.RoundCount = previous + 1;
        p.Status = next;
        p.LastActionAt = now;
        if (ProposalStateMachine.IsOpen(next)) p.ExpiresAt = now.AddDays(config.GetValue("Proposals:ExpiryDays", 14));
        else { p.ExpiresAt = null; p.ClosedAt = now; }
        if (terms is not null) ProposalTermsRules.Apply(p, terms);
        var round = new ProposalRound
        {
            Id = Guid.CreateVersion7(), ProposalId = p.Id, Number = p.RoundCount, By = role, Action = ProposalStateMachine.ActionOf(trigger),
            RateMinor = terms?.Rate.AmountMinor, HoursPerWeek = terms?.HoursPerWeek, TotalHours = terms?.TotalHours, DurationMonths = terms?.DurationMonths,
            StartDate = terms?.StartDate, Text = text, CreatedAt = now,
        };
        db.ProposalRounds.Add(round);
        p.Rounds.Add(round);
        ScheduleNotifications(notifications, p, trigger, role, now);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Invalid(); } // the other party answered at the same moment
        catch (DbUpdateException) when (db.ChangeTracker.Entries<ProposalRound>().Any(e => e.State == EntityState.Added)) { return Invalid(); } // the unique (proposal, round number) refused a second writer

        log.LogInformation("Proposal {ProposalId} {Trigger} by {Role}: {Status}", p.Id, trigger, role, p.Status);
        return Results.Ok(await views.ViewAsync(p, role, ct));
    }

    /// <summary>Who is told what (ADR proposal 0013 section 4): a counter and a reject go to the other party; an accept to both; an expiry is the job's. A withdrawal sends nothing.</summary>
    static void ScheduleNotifications(INotificationWriter writer, Proposal p, ProposalTrigger trigger, ProposalActor by, DateTimeOffset now)
    {
        var other = by == ProposalActor.Client ? p.PractitionerUserId : p.ClientUserId;
        switch (trigger)
        {
            case ProposalTrigger.Counter: writer.ScheduleProposal(p, NotificationKind.ProposalCountered, other, p.RoundCount, now); break;
            case ProposalTrigger.Reject: writer.ScheduleProposal(p, NotificationKind.ProposalRejected, other, p.RoundCount, now); break;
            case ProposalTrigger.Accept:
                writer.ScheduleProposal(p, NotificationKind.ProposalAccepted, p.PractitionerUserId, p.RoundCount, now);
                writer.ScheduleProposal(p, NotificationKind.ProposalAccepted, p.ClientUserId, p.RoundCount, now);
                break;
        }
    }
}
