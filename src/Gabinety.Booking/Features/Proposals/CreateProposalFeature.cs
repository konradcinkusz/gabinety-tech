using System.Security.Claims;
using System.Text.Json;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Bookings.View;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Features.Slots;
using Gabinety.Booking.Notifications;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gabinety.Booking.Features.Proposals;

/// <summary>
/// POST /api/v1/proposals (ADR proposal 0013): a signed-in client sends a collaboration proposal to a practitioner who advertises contract availability. Checks in this order: header and body (400),
/// terms and text (422), replay of the same <c>Idempotency-Key</c> (the unique key on the row, no response body is stored), the per-user limit (429), the target in Directory (404, 422, 503),
/// one open proposal per pair (409), then one save of the proposal, its first round and its notification. The text and the company name are free text: never logged, never in a problem detail.
/// </summary>
public sealed class CreateProposalFeature : IServiceFeature
{
    public const string IdempotencyHeader = "Idempotency-Key";
    const string UniqueViolation = "23505";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/proposals", HandleAsync).RequireAuthorization("Authenticated");

    internal static IResult Problem(string code, string title, int status, Dictionary<string, string[]>? errors = null) =>
        PaymentWebhookFeature.Problem(code, title, status, null, errors);

    internal static string? Sub(ClaimsPrincipal user) => BookingViewFeature.CallerId(user);

    internal static DateOnly TodayWarsaw(DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, SlotCalculator.Warsaw).DateTime);

    /// <summary>Reads a JSON body; null for an empty, malformed or wrongly shaped one.</summary>
    internal static async Task<T?> ReadAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        try { return await request.ReadFromJsonAsync<T>(Json, ct); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or BadHttpRequestException) { return null; }
    }

    /// <summary>The cleaned message (20 to 1000 characters when required) or the error; the contact-detail check is the caller's, so it can answer with its own code.</summary>
    internal static string? CleanText(string? raw, bool required, int max, string field, Dictionary<string, string[]> errors)
    {
        var clean = ProposalTextRules.Clean(raw);
        var min = required ? 20 : 0;
        if (clean.Length < min || clean.Length > max) errors[field] = [min > 0 ? $"{min} to {max} characters" : $"at most {max} characters"];
        return clean.Length == 0 ? null : clean;
    }

    static async Task<IResult> HandleAsync(HttpContext http, ClaimsPrincipal user, BookingDbContext db, IDirectoryClient directory, ProposalViewBuilder views,
        ProposalRateLimiter limiter, INotificationWriter notifications, TimeProvider time, IConfiguration config, ILogger<CreateProposalFeature> log, CancellationToken ct)
    {
        var sub = Sub(user);
        if (sub is null) return Results.Unauthorized();

        // 1. header and body
        if (!Guid.TryParse(http.Request.Headers[IdempotencyHeader].ToString(), out var key))
            return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { [IdempotencyHeader] = ["Idempotency-Key is required and must be a uuid"] });
        var req = await ReadAsync<CreateProposalRequest>(http.Request, ct);
        if (req is null) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, new() { ["body"] = ["a JSON body of a collaboration proposal"] });

        // 2. terms and text
        var now = time.GetUtcNow();
        var errors = ProposalTermsRules.Validate(req.Kind, req.Terms, TodayWarsaw(now));
        var text = CleanText(req.Text, required: true, 1000, "text", errors);
        var company = CleanText(req.CompanyName, required: false, 120, "companyName", errors);
        if (errors.Count > 0) return Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, errors);
        if (ProposalTextRules.HasContactDetails(text!) || (company is not null && ProposalTextRules.HasContactDetails(company)))
            return Problem(ErrorCodes.ContactDetailsNotAllowed, "Contact details are not allowed in a proposal", 422);
        var terms = ProposalTermsRules.Normalize(req.Kind, req.Terms);

        // 3. replay of the same key: the row is the record, so nothing of the request is stored twice
        var seen = await db.Proposals.AsNoTracking().Include(p => p.Rounds).SingleOrDefaultAsync(p => p.ClientUserId == sub && p.IdempotencyKey == key, ct);
        if (seen is not null) return await ReplayAsync(seen, req, views, ct);

        // 4. the limit
        if (!limiter.TryAcquire(sub, out var wait))
        {
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Problem(ErrorCodes.RateLimited, "Rate limited", 429);
        }

        // 5. the target
        DirectoryContractTarget? target;
        try { target = await directory.GetContractTargetAsync(req.PractitionerId, ct); }
        catch (DirectoryUnavailableException) { return Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503); }
        if (target is null) return Problem(ErrorCodes.NotFound, "Not found", 404);
        if (target.PractitionerUserId == sub) return Problem(ErrorCodes.OwnProfile, "Own profile", 422);
        if (!target.AcceptsProposals) return Problem(ErrorCodes.PractitionerNotAvailable, "Practitioner not available", 422);

        // 6. one open proposal per pair
        if (await OpenExistsAsync(db, sub, target.PractitionerId, ct)) return Problem(ErrorCodes.ProposalExists, "Proposal exists", 409);

        var days = config.GetValue("Proposals:ExpiryDays", 14);
        var proposal = new Proposal
        {
            Id = Guid.CreateVersion7(), ClientUserId = sub, ClientEmail = UserEmail.From(user),
            ClientDisplayName = PartyNamesLookup.ClientSnapshot(user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value),
            CompanyName = company, PractitionerId = target.PractitionerId, PractitionerUserId = target.PractitionerUserId,
            Kind = req.Kind, Status = ProposalStateMachine.Create(), RoundCount = 1, IdempotencyKey = key,
            CreatedAt = now, LastActionAt = now, ExpiresAt = now.AddDays(days),
        };
        ProposalTermsRules.Apply(proposal, terms);
        proposal.Rounds.Add(new ProposalRound
        {
            Id = Guid.CreateVersion7(), ProposalId = proposal.Id, Number = 1, By = ProposalActor.Client, Action = ProposalAction.Proposed,
            RateMinor = terms.Rate.AmountMinor, HoursPerWeek = terms.HoursPerWeek, TotalHours = terms.TotalHours, DurationMonths = terms.DurationMonths,
            StartDate = terms.StartDate, Text = text, CreatedAt = now,
        });
        db.Proposals.Add(proposal);
        notifications.ScheduleProposal(proposal, NotificationKind.ProposalReceived, proposal.PractitionerUserId, round: 1, dueAt: now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (IsUnique(e))
        {
            // Another request with the same key, or another open proposal of the pair, won the race: the unique indexes decide.
            db.ChangeTracker.Clear();
            var won = await db.Proposals.AsNoTracking().Include(p => p.Rounds).SingleOrDefaultAsync(p => p.ClientUserId == sub && p.IdempotencyKey == key, ct);
            return won is not null ? await ReplayAsync(won, req, views, ct) : Problem(ErrorCodes.ProposalExists, "Proposal exists", 409);
        }

        log.LogInformation("Proposal {ProposalId} created for practitioner {PractitionerId} ({Kind})", proposal.Id, proposal.PractitionerId, proposal.Kind);
        http.Response.Headers.Location = $"/api/v1/proposals/{proposal.Id}";
        return Results.Json(await views.ViewAsync(proposal, ProposalActor.Client, ct), Json, "application/json", 201);
    }

    static Task<bool> OpenExistsAsync(BookingDbContext db, string clientUserId, Guid practitionerId, CancellationToken ct) =>
        db.Proposals.AsNoTracking().AnyAsync(p => p.ClientUserId == clientUserId && p.PractitionerId == practitionerId
            && (p.Status == ProposalStatus.AwaitingPractitioner || p.Status == ProposalStatus.AwaitingClient), ct);

    /// <summary>The same key for the same practitioner and kind is a retry: the current view, 200. Anything else under that key is a conflict.</summary>
    static async Task<IResult> ReplayAsync(Proposal seen, CreateProposalRequest req, ProposalViewBuilder views, CancellationToken ct) =>
        seen.PractitionerId == req.PractitionerId && seen.Kind == req.Kind
            ? Results.Json(await views.ViewAsync(seen, ProposalActor.Client, ct), Json, "application/json", 200)
            : Problem(ErrorCodes.IdempotencyConflict, "Idempotency conflict", 409);

    static bool IsUnique(DbUpdateException e) => e.InnerException is PostgresException { SqlState: UniqueViolation };
}
