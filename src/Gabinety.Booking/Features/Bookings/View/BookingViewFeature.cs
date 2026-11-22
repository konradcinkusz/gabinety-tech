using System.Security.Claims;
using Gabinety.Billing;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Bookings.View;
using Gabinety.Booking.Features.Internal.Erasure;
using Gabinety.Booking.Features.Lifecycle.Cancel;
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
/// The party names come from <see cref="PartyNamesLookup"/> (ADR-0012 section 6): the practitioner's from Directory, the client's from the snapshot taken when the booking was made.
/// </summary>
public sealed class BookingViewFeature : IServiceFeature
{
    public void Add(IHostApplicationBuilder builder) => builder.Services.AddScoped<PartyNamesLookup>();

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/bookings/{id:guid}", GetAsync).RequireAuthorization("Authenticated");

    public static string? CallerId(ClaimsPrincipal user) => user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin") || user.IsInRole("SuperAdmin");

    static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal user, BookingDbContext db, PartyNamesLookup names, TimeProvider time, CancellationToken ct)
    {
        var sub = CallerId(user);
        if (sub is null) return Results.Unauthorized();
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
        var admin = IsAdmin(user);
        if (booking is null || (booking.ClientUserId != sub && booking.PractitionerUserId != sub && !admin))
            return PaymentWebhookFeature.Problem(ErrorCodes.NotFound, "Not found", 404); // a stranger learns nothing about whether it exists
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.BookingId == id, ct);
        return Results.Ok(ToView(booking, payment, sub, admin, time, (await names.ForAsync([booking], ct))[booking.Id]));
    }

    /// <summary>The admin's resolution note is stored in <c>status_reason</c> of a Resolved booking and is for the admin only (CONTRACTS 4.3); a cancel reason is for both parties.</summary>
    static string? StatusReasonFor(BookingEntity b, bool admin) => b.Status == Gabinety.Billing.BookingStatus.Resolved && !admin ? null : b.StatusReason;

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
            b.Id, Enum.Parse<ContractsStatus>(b.Status.ToString()), StatusReasonFor(b, admin), b.StartUtc, b.EndUtc, b.DurationMinutes, b.OfferingTitle,
            new MoneyDto(b.PriceMinor), new PractitionerRef(b.PractitionerId, names?.PractitionerDisplayName ?? PartyNamesLookup.PractitionerFallback, names?.PractitionerSlug ?? ""),
            isPractitioner && wasConfirmed && names?.ClientDisplayName is { } clientName ? new ClientRef(clientName) : null,
            isClient || (isPractitioner && wasConfirmed) ? new BriefDto(b.BriefText, b.BriefProblemSlug, b.BriefAiAssisted) : null,
            confirmed && (isClient || isPractitioner) ? b.MeetingUrlSnapshot : null,
            b.Status == Gabinety.Billing.BookingStatus.PendingPayment ? b.HoldExpiresAt : null,
            new PaymentView(payment?.Status ?? PaymentStatus.Created, new MoneyDto(payment?.RefundedMinor ?? 0)),
            isPractitioner || admin ? money(b.FeeMinor) : null, isPractitioner || admin ? money(b.PayoutMinor) : null,
            flags.CanCancel, flags.CanDispute, flags.CanReportNoShow, flags.CanReview,
            // The same planner the cancel endpoint runs, so what the dialog says is what the money does.
            flags.CanCancel && CancelPlanner.Plan(b, byClient: isClient, time?.GetUtcNow() ?? TimeProvider.System.GetUtcNow()) is { } plan ? plan.ToDto() : null);
    }
}

/// <summary>Display names for one booking; every member is optional and absent means "not known" (the view then shows its fallback).</summary>
public sealed record PartyNames(string? PractitionerDisplayName = null, string? PractitionerSlug = null, string? ClientDisplayName = null);

/// <summary>
/// The counter-party names of bookings (ADR-0012 section 6, F-03). The practitioner's name and slug come from Directory (one call per distinct practitioner,
/// the 3 s budget of <see cref="DirectoryClient"/>); when Directory cannot answer the view still renders, with <see cref="PractitionerFallback"/>. The client's name is the
/// snapshot on the booking: "Klient" and an initial, never an e-mail or a surname; <see cref="ErasedName"/> once either side was erased.
/// </summary>
public sealed class PartyNamesLookup(IDirectoryClient directory)
{
    public const string ErasedName = "Usunięty użytkownik", ClientFallback = "Klient", PractitionerFallback = "Programista";

    public async Task<Dictionary<Guid, PartyNames>> ForAsync(IReadOnlyCollection<BookingEntity> rows, CancellationToken ct)
    {
        var live = rows.Where(b => !IsErased(b.PractitionerUserId)).Select(b => b.PractitionerId).Distinct().ToList();
        var found = new Dictionary<Guid, DirectoryPractitionerName>();
        var lookups = await Task.WhenAll(live.Select(async id =>
        {
            try { return await directory.GetPractitionerNameAsync(id, ct); }
            catch (DirectoryUnavailableException) { return null; }
        }));
        foreach (var n in lookups) if (n is not null) found[n.PractitionerId] = n;

        return rows.ToDictionary(b => b.Id, b => new PartyNames(
            IsErased(b.PractitionerUserId) ? ErasedName : found.TryGetValue(b.PractitionerId, out var n) ? n.DisplayName : null,
            IsErased(b.PractitionerUserId) ? "" : found.TryGetValue(b.PractitionerId, out var m) ? m.Slug : null,
            IsErased(b.ClientUserId) ? ErasedName : b.ClientDisplayName ?? ClientFallback));
    }

    static bool IsErased(string userId) => userId.StartsWith(ErasureFeature.TokenPrefix, StringComparison.Ordinal);

    /// <summary>"Klient D." from a token's name claim: the first letter or digit, upper-cased. No claim, or none that holds a letter: "Klient". The rest of the claim (it is the e-mail in authservice) is dropped.</summary>
    public static string ClientSnapshot(string? nameClaim)
    {
        var initial = nameClaim?.FirstOrDefault(char.IsLetterOrDigit);
        return initial is { } c && c != default ? $"{ClientFallback} {char.ToUpperInvariant(c)}." : ClientFallback;
    }
}
