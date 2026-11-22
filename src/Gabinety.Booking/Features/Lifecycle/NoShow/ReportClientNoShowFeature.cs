using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Lifecycle.Core;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;

namespace Gabinety.Booking.Features.Lifecycle.NoShow;

/// <summary>
/// POST /api/v1/bookings/{id}/report-client-no-show (CONTRACTS 4.2, 8.1, 9.2). Only the practitioner of the booking; anyone else gets 404.
/// Allowed from start + 15 min until start + 72 h. The booking counts as completed for money: no refund, <c>payout_due_at</c> is the report time
/// (the payout job of T-04.08 computes fee and payout), and there is no review.
/// </summary>
public sealed class ReportClientNoShowFeature : IServiceFeature
{
    public static readonly TimeSpan Earliest = TimeSpan.FromMinutes(15), Latest = TimeSpan.FromHours(72);

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/v1/bookings/{id:guid}/report-client-no-show", ReportAsync).RequireAuthorization("Authenticated");

    /// <summary>The lower edge is inclusive and the upper exclusive, as for the dispute window.</summary>
    public static bool InWindow(BookingEntity b, DateTimeOffset now) => now >= b.StartUtc + Earliest && now < b.StartUtc + Latest;

    static async Task<IResult> ReportAsync(Guid id, ClaimsPrincipal user, BookingDbContext db, BookingTransitions transitions, PartyNamesLookup names, TimeProvider time, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();

        var ownerId = await db.Bookings.AsNoTracking().Where(b => b.Id == id).Select(b => b.PractitionerUserId).SingleOrDefaultAsync(ct);
        if (ownerId != sub) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);

        var now = time.GetUtcNow();
        var result = await transitions.ApplyAsync(new TransitionRequest(id, BookingTrigger.ClientNoShowReported, now)
        {
            Precondition = b => InWindow(b, now),
            Effects = c => c.Booking.PayoutDueAt = now,
        }, ct);
        return result.ToErrorResult() ?? Results.Ok(BookingViewFeature.ToView(result.Booking!, result.Payment, sub, admin: false, names: (await names.ForAsync([result.Booking!], ct))[result.Booking!.Id]));
    }
}
