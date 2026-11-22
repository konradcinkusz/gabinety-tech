using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Features.Admin.Disputes;

/// <summary>Body of resolve (CONTRACTS 4.3). Every member is nullable so a missing one is a 400 of ours, not a binder error.</summary>
public sealed record ResolveRequest(string? Resolution, long? RefundMinor, string? Note);

/// <summary>One row of the admin list. <see cref="Text"/> and <see cref="Note"/> are user and admin free text: this list is the only response that carries them, and nothing logs them.</summary>
public sealed record AdminDisputeItem(
    Guid BookingId, string ClientUserId, string PractitionerUserId, string? Kind, string? Text,
    [property: JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? OpenedAt,
    [property: JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? ResolvedAt,
    string? Resolution, string? Note);

/// <summary>
/// The admin side of disputes (CONTRACTS 4.3, 8.1, 9.2; D-25), both endpoints behind the <c>Admin</c> policy.
/// <list type="bullet">
/// <item><c>GET /api/v1/admin/disputes?status=open|resolved</c>: open = status <c>Disputed</c>, resolved = <c>Resolved</c>; newest first by (opened or resolved time, id)
/// with an opaque cursor like the other lists.</item>
/// <item><c>POST /api/v1/admin/bookings/{id}/resolve</c>: <c>AdminResolves</c>, from <c>Disputed</c> only. The refund is requested at the provider BEFORE the
/// transaction (no lock across a network call) and recorded inside it by <see cref="RefundCoordinator.Record"/>, which runs in the effects, i.e. before
/// every <see cref="IBookingTransitionHook"/>: a hook (the fee on the kept amount, T-04.08c) sees <c>payment.RefundedMinor</c> already set.</item>
/// </list>
/// The dispute text and the note are never logged. The resolve response is the BookingView without the note (<c>statusReason</c> is cleared).
/// </summary>
public sealed class AdminDisputesFeature : IServiceFeature
{
    public const int MinNoteLength = 10, MaxNoteLength = 500, DefaultLimit = 20, MaxLimit = 50;
    public static readonly string[] Resolutions = ["release", "refund_full", "refund_partial"];

    // RefundReason has no member for a dispute yet (adding one is an edit of Lifecycle/Core). The value only feeds the provider idempotency key,
    // "refund:<booking>:<reason>": a booking is resolved once, so any value distinct from the cancellation reasons gives one refund per booking.
    static readonly RefundReason DisputeResolvedReason = (RefundReason)2;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/admin/disputes", ListAsync).RequireAuthorization("Admin");
        endpoints.MapPost("/api/v1/admin/bookings/{id:guid}/resolve", ResolveAsync).RequireAuthorization("Admin");
    }

    // ---- list ----

    static async Task<IResult> ListAsync(string? status, string? cursor, int? limit, BookingDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var resolved = false;
        if (status is not null && status is not ("open" or "resolved")) errors["status"] = ["The status is open or resolved."];
        else resolved = status == "resolved";
        if (limit is < 1) errors["limit"] = ["The limit is at least 1."];
        DateTimeOffset at = default; Guid id = default;
        if (cursor is not null && !TryDecode(cursor, out at, out id)) errors["cursor"] = ["The cursor is not valid."];
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors);

        var take = Math.Min(limit ?? DefaultLimit, MaxLimit);
        var want = resolved ? BookingStatus.Resolved : BookingStatus.Disputed;
        var q = db.Bookings.AsNoTracking().Where(b => b.Status == want)
            .Select(b => new
            {
                Key = resolved ? (b.ResolvedAt ?? b.DisputeOpenedAt ?? b.CreatedAt) : (b.DisputeOpenedAt ?? b.CreatedAt),
                Item = new AdminDisputeItem(b.Id, b.ClientUserId, b.PractitionerUserId, b.DisputeKind, b.DisputeText, b.DisputeOpenedAt, b.ResolvedAt,
                    b.DisputeResolution, resolved ? b.StatusReason : null),
            });
        if (cursor is not null) q = q.Where(x => x.Key < at || (x.Key == at && x.Item.BookingId.CompareTo(id) < 0));
        var rows = await q.OrderByDescending(x => x.Key).ThenByDescending(x => x.Item.BookingId).Take(take + 1).ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(take);
        return Results.Json(new Page<AdminDisputeItem>([.. rows.Select(r => r.Item)], more ? Encode(rows[^1].Key, rows[^1].Item.BookingId) : null));
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

    // ---- resolve ----

    static async Task<IResult> ResolveAsync(Guid id, ResolveRequest? body, ClaimsPrincipal user, BookingDbContext db, BookingTransitions transitions,
        RefundCoordinator refunds, PartyNamesLookup names, TimeProvider time, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);

        var errors = Validate(body, booking.PriceMinor);
        if (errors.Count > 0) return PaymentWebhookFeature.Problem(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors);
        var resolution = body!.Resolution!;
        var note = body.Note!.Trim();

        // The state table decides under the lock; this read only avoids calling the provider for a booking that cannot be resolved.
        if (booking.Status != BookingStatus.Disputed) return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);

        var refund = resolution switch { "refund_full" => booking.PriceMinor, "refund_partial" => body.RefundMinor!.Value, _ => 0L };
        RefundTicket? ticket = null;
        if (refund > 0)
        {
            var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
            // A concurrent resolve may have refunded already (the booking read above is unlocked): answer 409 without calling the provider.
            if (payment is null || payment.RefundedMinor + refund > payment.AmountMinor) return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409);
            try { ticket = await refunds.RequestAsync(id, payment, refund, DisputeResolvedReason, ct); }
            catch (InvalidOperationException) { return PaymentWebhookFeature.Problem(ErrorCodes.InvalidTransition, "Invalid transition", 409); } // nothing left to refund
            catch (HttpRequestException) { return Unavailable(); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Unavailable(); }
        }

        var now = time.GetUtcNow();
        var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.AdminResolves, now)
        {
            Effects = c =>
            {
                // The stats push (T-04.10) derives the practitioner no-show counter from the stored rows: it counts bookings with status Resolved,
                // dispute_resolution = refund_full and dispute_kind = practitioner_no_show. Nothing extra is written here for it.
                c.Booking.DisputeResolution = resolution;
                c.Booking.ResolvedAt = now;
                c.Booking.PayoutDueAt = now; // the dispute is over: a resolved booking is paid out at once (ADR proposal 0011); the payout job also falls back to resolved_at for older rows
                c.Booking.StatusReason = note;
                // Recorded here, before the hooks run, so a hook (the fee on the kept amount, T-04.08c) reads payment.RefundedMinor already set.
                // With a provider that confirms later (not the Mock) nothing is recorded now: the RefundSucceeded webhook records it, and the fee for
                // refund_partial must then be settled at that confirmation, because the refunded amount is not known when the hooks run.
                if (ticket is not null) RefundCoordinator.Record(c, ticket);
            },
        }, ct);
        if (result.ToErrorResult() is { } error) return error;
        return Results.Ok(BookingViewFeature.ToView(result.Booking!, result.Payment, sub, admin: true, names: (await names.ForAsync([result.Booking!], ct))[result.Booking!.Id]) with { StatusReason = null }); // the note is for the admin list only
    }

    static Dictionary<string, string[]> Validate(ResolveRequest? body, long price)
    {
        var errors = new Dictionary<string, string[]>();
        if (body?.Resolution is null || !Resolutions.Contains(body.Resolution)) errors["resolution"] = [$"The resolution is one of: {string.Join(", ", Resolutions)}."];
        if (body?.Note?.Trim() is not { Length: >= MinNoteLength and <= MaxNoteLength }) errors["note"] = [$"The note is {MinNoteLength} to {MaxNoteLength} characters."];
        if (body?.Resolution == "refund_partial")
        {
            if (body.RefundMinor is not { } r || r < 1 || r > price - 1) errors["refundMinor"] = [$"The refund is 1 to {price - 1} grosz."];
        }
        else if (body?.RefundMinor is not null && body.Resolution is not null && Resolutions.Contains(body.Resolution))
            errors["refundMinor"] = ["The refund amount is only for refund_partial."];
        return errors;
    }

    static IResult Unavailable() =>
        PaymentWebhookFeature.Problem(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "The payment provider is temporarily unavailable.");
}
