using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using ContractsStatus = Gabinety.Contracts.Booking.BookingStatus;

namespace Gabinety.Booking.Features.Payments;

/// <summary>
/// GET /api/v1/bookings/{id} (CONTRACTS 4.2, 4.4), the minimum T-04.06 needs to show the confirmation. The listing, the cancel/dispute flags and the party
/// display names come with T-04.07 and the Directory lookups: until then <c>practitioner.displayName</c> and <c>slug</c> are empty, <c>client</c> is null
/// and the three <c>can*</c> flags are false.
/// </summary>
public sealed class BookingViewFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/bookings/{id:guid}", GetAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, BookingDbContext db, CancellationToken ct)
    {
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (sub is null) return Results.Unauthorized();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        var admin = user.IsInRole("Admin") || user.IsInRole("SuperAdmin");
        if (booking is null || (booking.ClientUserId != sub && booking.PractitionerUserId != sub && !admin))
            return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // a stranger learns nothing about whether it exists
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
        return Results.Ok(ToView(booking, payment, sub, admin));
    }

    /// <summary>The meeting URL is for a party of a Confirmed booking only; the brief for the client always and for the practitioner once Confirmed.</summary>
    public static BookingView ToView(BookingEntity b, Payment? payment, string callerId, bool admin)
    {
        var isClient = b.ClientUserId == callerId;
        var isPractitioner = b.PractitionerUserId == callerId;
        var confirmed = b.Status == Gabinety.Billing.BookingStatus.Confirmed;
        var money = (long? m) => m is { } v ? new MoneyDto(v) : null;
        return new BookingView(
            b.Id, Enum.Parse<ContractsStatus>(b.Status.ToString()), b.StatusReason, b.StartUtc, b.EndUtc, b.DurationMinutes, b.OfferingTitle,
            new MoneyDto(b.PriceMinor), new PractitionerRef(b.PractitionerId, "", ""), null,
            isClient || (isPractitioner && confirmed) ? new BriefDto(b.BriefText, b.BriefProblemSlug, b.BriefAiAssisted) : null,
            confirmed && (isClient || isPractitioner) ? b.MeetingUrlSnapshot : null,
            b.Status == Gabinety.Billing.BookingStatus.PendingPayment ? b.HoldExpiresAt : null,
            new PaymentView(payment?.Status ?? PaymentStatus.Created, new MoneyDto(payment?.RefundedMinor ?? 0)),
            isPractitioner || admin ? money(b.FeeMinor) : null, isPractitioner || admin ? money(b.PayoutMinor) : null,
            false, false, false, false);
    }
}
