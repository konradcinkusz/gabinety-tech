using System.Globalization;

using Gabinety.Booking.Clients;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Slots;

/// <summary>GET /api/v1/practitioners/{practitionerId}/slots (CONTRACTS 4.2): public, computed by Gabinety.Scheduling.</summary>
public sealed class SlotsFeature : IServiceFeature
{
    public const int MaxRangeDays = 14;

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

        var duration = TimeSpan.FromMinutes(found.DurationMinutes);
        var starts = await new SlotCalculator(db, config, time).ComputeAsync(practitionerId, duration, fromDate!.Value, toDate!.Value, ct);
        return Results.Json(new SlotsResponse(starts.Select(s => new SlotDto(s, s + duration)).ToList()));
    }

    static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    static IResult Problem(ApiProblem p) => Results.Json(p, statusCode: p.Status, contentType: "application/problem+json");
}
