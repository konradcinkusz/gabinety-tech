using BookingStatus = Gabinety.Billing.BookingStatus;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Internal.Reviewability;

/// <summary>
/// GET /internal/v1/bookings/{id}/reviewability (CONTRACTS 4.3), asked by Reputation. Reviewable only for <c>Completed</c>, or <c>Resolved</c> with
/// <c>dispute_resolution = release</c>, and only until 14 days after <c>ended_at</c> (inclusive). Until the job that sets <c>ended_at</c> exists the
/// window runs from <c>end_utc</c>. The answer carries no brief and no price.
/// </summary>
public sealed class ReviewabilityFeature : IServiceFeature
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(14);

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/internal/v1/bookings/{id:guid}/reviewability", GetAsync).RequireAuthorization("Internal");

    static async Task<IResult> GetAsync(Guid id, BookingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Status, x.DisputeResolution, x.EndedAt, x.EndUtc, x.ClientUserId, x.PractitionerId })
            .SingleOrDefaultAsync(ct);
        if (b is null) return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404);

        var ended = b.EndedAt ?? b.EndUtc;
        var eligible = b.Status == BookingStatus.Completed
            || (b.Status == BookingStatus.Resolved && b.DisputeResolution == "release");
        var reviewable = eligible && clock.GetUtcNow() <= ended + Window;
        return Results.Ok(new ReviewabilityDto(reviewable, b.ClientUserId, b.PractitionerId, ended));
    }
}
