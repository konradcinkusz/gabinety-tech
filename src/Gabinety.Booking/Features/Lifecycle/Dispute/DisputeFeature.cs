using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Features.Lifecycle.Dispute;

/// <summary>Body of the dispute call (CONTRACTS 4.2). Both members are nullable so a missing one is a 422 of ours, not a binder error.</summary>
public sealed record DisputeRequest(string? Kind, string? Text);

/// <summary>
/// POST /api/v1/bookings/{id}/dispute (CONTRACTS 4.2, 8.1). Only the client of the booking; anyone else gets 404 (before the body is looked at).
/// The text is user free text: it is stored in <c>dispute_text</c> and appears in no log, metric, exception or response (the BookingView does not carry it).
/// The window is the precondition passed to <see cref="BookingTransitions"/>: end to end + 48 h, or for <c>practitioner_no_show</c> start to start + 72 h.
/// </summary>
public sealed class DisputeFeature : IServiceFeature
{
    public const int MinTextLength = 10, MaxTextLength = 1000;
    public static readonly TimeSpan Window = TimeSpan.FromHours(48), NoShowWindow = TimeSpan.FromHours(72);
    public static readonly string[] Kinds = ["not_as_described", "practitioner_no_show", "other"];

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/bookings/{id:guid}/dispute", DisputeAsync).RequireAuthorization("Authenticated");

    /// <summary>The lower edge is inclusive and the upper exclusive: at end + 48 h the booking auto-completes (T-04.08), so the two never both hold.</summary>
    public static bool InWindow(BookingEntity b, string kind, DateTimeOffset now)
    {
        var end = b.EndedAt ?? b.EndUtc;
        if (now >= end && now < end + Window) return true;
        return kind == "practitioner_no_show" && now >= b.StartUtc && now < b.StartUtc + NoShowWindow;
    }

    static async Task<IResult> DisputeAsync(Guid id, DisputeRequest? body, ClaimsPrincipal user, BookingDbContext db, BookingTransitions transitions,
        TimeProvider time, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        var ownerId = await db.Bookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.ClientUserId).SingleOrDefaultAsync(ct);
        if (ownerId != sub) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // unknown, or not the client: the same answer

        var errors = new Dictionary<string, string[]>();
        var kind = body?.Kind;
        if (kind is null || !Kinds.Contains(kind)) errors["kind"] = [$"The kind is one of: {string.Join(", ", Kinds)}."];
        var text = body?.Text?.Trim();
        if (text is null || text.Length is < MinTextLength or > MaxTextLength) errors["text"] = [$"The text is {MinTextLength} to {MaxTextLength} characters."];
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 422, null, errors);

        var now = time.GetUtcNow();
        var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.ClientDisputes, now)
        {
            Precondition = b => InWindow(b, kind!, now),
            Effects = c =>
            {
                c.Booking.DisputeOpenedAt = now;
                c.Booking.DisputeKind = kind;
                c.Booking.DisputeText = text;
            },
        }, ct);
        return result.ToErrorResult() ?? Results.Ok(BookingViewFeature.ToView(result.Booking!, result.Payment, sub, admin: false));
    }
}
