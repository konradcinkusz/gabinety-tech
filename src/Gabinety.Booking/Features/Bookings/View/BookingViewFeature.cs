using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Bookings.View;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using ContractsStatus = Gabinety.Contracts.Booking.BookingStatus;

namespace Gabinety.Booking.Features.Payments;

/// <summary>
/// GET /api/v1/bookings/{id} (CONTRACTS 4.2, 4.4). The list is <see cref="MyBookingsFeature"/>; both build their items with <see cref="ToView"/>.
/// Still stubbed: the party names. <c>practitioner.displayName</c> and <c>slug</c> are empty and <c>client</c> is null until Directory can look them up
/// (CONTRACTS 3.6 has no such call: an open question to the owner). Pass <see cref="PartyNames"/> to <see cref="ToView"/> when it can.
/// </summary>
public sealed class BookingViewFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/bookings/{id:guid}", GetAsync).RequireAuthorization("Authenticated");

    public static string? CallerId(ClaimsPrincipal user) => user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin") || user.IsInRole("SuperAdmin");

    static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, BookingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var sub = CallerId(user);
        if (sub is null) return Results.Unauthorized();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        var admin = IsAdmin(user);
        if (booking is null || (booking.ClientUserId != sub && booking.PractitionerUserId != sub && !admin))
            return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // a stranger learns nothing about whether it exists
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
        return Results.Ok(ToView(booking, payment, sub, admin, time));
    }

    /// <summary>
    /// The meeting URL is for a party of a Confirmed booking only; the brief for the client always and for the practitioner from Confirmed onward (any status but PendingPayment and Expired); fee and payout for
    /// the practitioner and admin. The flags are the caller's at <paramref name="time"/> (<see cref="TimeProvider.System"/> when omitted: the callers that
    /// pass nothing build the view of a booking that has just left Confirmed, where every flag is false whatever the clock says).
    /// </summary>
    public static BookingView ToView(BookingEntity b, Payment? payment, string callerId, bool admin, TimeProvider? time = null, PartyNames? names = null)
    {
        var isClient = b.ClientUserId == callerId;
        var isPractitioner = b.PractitionerUserId == callerId;
        var confirmed = b.Status == Gabinety.Billing.BookingStatus.Confirmed;
        // CONTRACTS 4.4 "after Confirmed": the practitioner sees the brief and the client name once the booking was confirmed at some point, i.e. from any
        // status but PendingPayment and Expired. Decided by status, not by confirmed_at (not every row sets it).
        var wasConfirmed = b.Status is not (Gabinety.Billing.BookingStatus.PendingPayment or Gabinety.Billing.BookingStatus.Expired);
        var money = (long? m) => m is { } v ? new MoneyDto(v) : null;
        var flags = BookingPermissions.For(b, callerId, time ?? TimeProvider.System);
        return new BookingView(
            b.Id, Enum.Parse<ContractsStatus>(b.Status.ToString()), b.StatusReason, b.StartUtc, b.EndUtc, b.DurationMinutes, b.OfferingTitle,
            new MoneyDto(b.PriceMinor), new PractitionerRef(b.PractitionerId, names?.PractitionerDisplayName ?? "", names?.PractitionerSlug ?? ""),
            isPractitioner && wasConfirmed && names?.ClientDisplayName is { } clientName ? new ClientRef(clientName) : null,
            isClient || (isPractitioner && wasConfirmed) ? new BriefDto(b.BriefText, b.BriefProblemSlug, b.BriefAiAssisted) : null,
            confirmed && (isClient || isPractitioner) ? b.MeetingUrlSnapshot : null,
            b.Status == Gabinety.Billing.BookingStatus.PendingPayment ? b.HoldExpiresAt : null,
            new PaymentView(payment?.Status ?? PaymentStatus.Created, new MoneyDto(payment?.RefundedMinor ?? 0)),
            isPractitioner || admin ? money(b.FeeMinor) : null, isPractitioner || admin ? money(b.PayoutMinor) : null,
            flags.CanCancel, flags.CanDispute, flags.CanReportNoShow, flags.CanReview);
    }
}

/// <summary>Display names a later Directory lookup can supply; every member is optional and absent means "not known".</summary>
public sealed record PartyNames(string? PractitionerDisplayName = null, string? PractitionerSlug = null, string? ClientDisplayName = null);
