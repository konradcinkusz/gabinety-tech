using System.Globalization;

using Gabinety.Booking.Clients;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Scheduling;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using BookingStatus = Gabinety.Billing.BookingStatus;
using SchedulingException = Gabinety.Scheduling.AvailabilityException;
using SchedulingRule = Gabinety.Scheduling.AvailabilityRule;

namespace Gabinety.Booking.Features.Slots;

/// <summary>GET /api/v1/practitioners/{practitionerId}/slots (CONTRACTS 4.2): public, computed by Gabinety.Scheduling.</summary>
public sealed class SlotsFeature : IServiceFeature
{
    public const int MaxRangeDays = 14;
    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/practitioners/{practitionerId:guid}/slots", HandleAsync).AllowAnonymous();

    static async Task<IResult> HandleAsync(Guid practitionerId, string? offeringId, string? from, string? to, HttpContext http,
        BookingDbContext db, IDirectoryClient directory, TimeProvider time, IConfiguration config, CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";

        var errors = new Dictionary<string, string[]>();
        if (!Guid.TryParse(offeringId, out var offering)) errors["offeringId"] = ["offeringId is required and must be a GUID"];
        var fromDate = ParseDate(from);
        var toDate = ParseDate(to);
        if (fromDate is null) errors["from"] = ["from is required, yyyy-MM-dd"];
        if (toDate is null) errors["to"] = ["to is required, yyyy-MM-dd"];
        if (fromDate is { } f && toDate is { } t)
        {
            if (t < f) errors["to"] = ["to must not be before from"];
            else if (t.DayNumber - f.DayNumber + 1 > MaxRangeDays) errors["to"] = [$"the range is at most {MaxRangeDays} days"];
        }
        if (errors.Count > 0) return Problem(ApiProblem.Of(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors));

        DirectoryOffering? found;
        try { found = await directory.GetOfferingAsync(offering, ct); }
        catch (DirectoryUnavailableException)
        {
            return Problem(ApiProblem.Of(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "A required service is temporarily unavailable."));
        }
        if (found is null || found.PractitionerId != practitionerId)
            return Problem(ApiProblem.Of(ErrorCodes.NotFound, "Not found", 404));
        if (!found.IsActive)
            return Problem(ApiProblem.Of(ErrorCodes.OfferingInactive, "Offering inactive", 422));
        if (found.PractitionerStatus != PractitionerStatus.Approved)
            return Problem(ApiProblem.Of(ErrorCodes.PractitionerNotBookable, "Practitioner not bookable", 422));

        var now = time.GetUtcNow();
        var windowStart = new DateTimeOffset(fromDate!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var windowEnd = new DateTimeOffset(toDate!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(2);

        var rules = (await db.Rules.AsNoTracking().Where(r => r.PractitionerId == practitionerId).ToListAsync(ct))
            .Select(r => new SchedulingRule(r.IsoWeekday, r.StartLocal, r.EndLocal)).ToList();
        var days = (await db.Exceptions.AsNoTracking().Where(e => e.PractitionerId == practitionerId).ToListAsync(ct))
            .Select(e => new SchedulingException(e.DateLocal)).ToList();

        // An EXPIRED hold does not block here (the database constraint still counts it until it is moved to Expired: see BookingSlotGuard).
        var busy = (await db.Bookings.AsNoTracking()
                .Where(b => b.PractitionerId == practitionerId && b.StartUtc < windowEnd && b.EndUtc > windowStart
                    && (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Disputed
                        || (b.Status == BookingStatus.PendingPayment && b.HoldExpiresAt > now)))
                .Select(b => new { b.StartUtc, b.EndUtc }).ToListAsync(ct))
            .Select(b => new BusyInterval(b.StartUtc, b.EndUtc)).ToList();

        var options = new SlotOptions(
            MinLeadHours: config.GetValue("Booking:MinLeadHours", 12),
            HorizonDays: config.GetValue("Booking:HorizonDays", 60),
            BufferMinutes: config.GetValue("Booking:BufferMinutes", 15));
        var duration = TimeSpan.FromMinutes(found.DurationMinutes);

        var starts = SlotGenerator.Generate(rules, days, duration, busy, now, fromDate.Value, toDate.Value, options, Warsaw);
        return Results.Json(new SlotsResponse(starts.Select(s => new SlotDto(s, s + duration)).ToList()));
    }

    static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    static IResult Problem(ApiProblem p) => Results.Json(p, statusCode: p.Status, contentType: "application/problem+json");
}
