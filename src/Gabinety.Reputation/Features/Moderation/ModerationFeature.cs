using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Moderation;

/// <summary>One row of the moderator's queue. The reviewer's identity is not part of it: moderating needs the text and the reasons, not the author.</summary>
public sealed record AdminReviewRow(Guid Id, Guid PractitionerId, int Rating, string Text, ReviewStatus Status, IReadOnlyList<string> HoldReasons,
    string TriageFlag, string? TriageModelId,
    [property: JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset CreatedAt,
    [property: JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? PublishedAt,
    string? ModeratedBy, string? ModerationNote);

/// <summary>
/// The admin moderation routes of CONTRACTS 5.2 (<c>Admin</c> policy: the WS-Federation role claim of authservice; a practitioner, a client or a
/// signed-out caller never reaches a handler). Review text and moderator notes are user free text: they appear in the list response only, never in a
/// log, an exception message, an error body or a trace.
/// </summary>
public sealed class ModerationFeature : IServiceFeature
{
    const int DefaultLimit = 20, MaxLimit = 50, MinReasonLength = 10, MaxReasonLength = 500; // the reject reason is 10 to 500 here as for practitioner verification (supervisor decision, ADR-0012)

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/reviews").RequireAuthorization("Admin");
        admin.MapGet("", ListAsync);
        admin.MapPost("{id:guid}/publish", (Guid id, ClaimsPrincipal u, ReputationDbContext db, TimeProvider t, ILogger<ModerationFeature> log, CancellationToken ct) =>
            DecideAsync(id, ModerationAction.Publish, null, u, db, t, log, ct));
        admin.MapPost("{id:guid}/reject", (Guid id, RejectReviewRequest? req, ClaimsPrincipal u, ReputationDbContext db, TimeProvider t, ILogger<ModerationFeature> log, CancellationToken ct) =>
        {
            var reason = req?.Reason?.Trim();
            return reason is null || reason.Length < MinReasonLength || reason.Length > MaxReasonLength
                ? Task.FromResult(Problem(422, ErrorCodes.ValidationFailed, "Validation failed", new() { ["reason"] = [$"reason is required, {MinReasonLength} to {MaxReasonLength} characters"] }))
                : DecideAsync(id, ModerationAction.Reject, reason, u, db, t, log, ct);
        });
    }

    static async Task<IResult> DecideAsync(Guid id, ModerationAction action, string? note, ClaimsPrincipal user, ReputationDbContext db, TimeProvider time,
        ILogger<ModerationFeature> log, CancellationToken ct)
    {
        var actor = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (actor is null) return Results.Unauthorized();

        var (outcome, practitionerId, status) = await ReviewModerator.ApplyAsync(db, id, action, actor, note, time.GetUtcNow(), ct);
        switch (outcome)
        {
            case ModerationOutcome.NotFound: return Problem(404, ErrorCodes.NotFound, "Not found");
            case ModerationOutcome.InvalidTransition: return Problem(409, ErrorCodes.InvalidTransition, "Invalid transition");
        }
        log.LogInformation("Review {ReviewId} of practitioner {PractitionerId} moved to {Status} by {Actor}", id, practitionerId, status, actor); // ids and a status only
        return Results.NoContent();
    }

    /// <summary>The queue, oldest first by (created_at, id), so a moderator works through it in the order the reviews arrived. Opaque cursor.</summary>
    static async Task<IResult> ListAsync(string? status, string? cursor, int? limit, ReputationDbContext db, CancellationToken ct)
    {
        var wanted = ReviewStatus.Held;
        if (status is not null && !(Enum.TryParse(status, true, out wanted) && Enum.IsDefined(wanted) && !char.IsDigit(status[0])))
            return Problem(422, ErrorCodes.ValidationFailed, "Validation failed", new() { ["status"] = ["status must be one of Held, Published, Rejected"] });
        var q = db.Reviews.AsNoTracking().Where(x => x.Status == wanted);
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out var at, out var id))
                return Problem(400, ErrorCodes.ValidationFailed, "Validation failed", new() { ["cursor"] = ["cursor is not valid"] });
            q = q.Where(x => x.CreatedAt > at || (x.CreatedAt == at && x.Id.CompareTo(id) > 0));
        }
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var rows = await q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take + 1).ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(take);
        var items = rows.Select(x => new AdminReviewRow(x.Id, x.PractitionerId, x.Rating, x.Text, x.Status, x.HoldReasons, x.TriageFlag.ToString().ToLowerInvariant(),
            x.TriageModelId, x.CreatedAt, x.PublishedAt, x.ModeratedBy, x.ModerationNote)).ToList();
        return Results.Json(new Page<AdminReviewRow>(items, more ? Encode(rows[^1].CreatedAt, rows[^1].Id) : null));
    }

    static string Encode(DateTimeOffset at, Guid id) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes($"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}.{id:N}"));

    static bool TryDecode(string cursor, out DateTimeOffset at, out Guid id)
    {
        at = default; id = default;
        try
        {
            var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id) || ticks > DateTime.MaxValue.Ticks) return false;
            at = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException) { return false; }
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");
}
