using System.Security.Claims;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gabinety.Directory.Features.ContractAvailability;

/// <summary>
/// /api/v1/me/practitioner/contract-availability (ADR proposal 0013, T-13.01): the practitioner says whether they take collaboration proposals, from when and for how many
/// hours a week. A separate endpoint on purpose: the profile PUT replaces every field it knows, so these four columns must not ride on it. Allowed in any profile status; the
/// public card shows them only for an Approved profile (the public reads already require that).
/// </summary>
public sealed class ContractAvailabilityFeature : IServiceFeature
{
    const string Route = "/api/v1/me/practitioner/contract-availability";

    public void Add(IHostApplicationBuilder builder) => builder.Services.TryAddSingleton(TimeProvider.System);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup(Route).RequireAuthorization("Authenticated");
        g.MapGet("", Get);
        g.MapPut("", Put);
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");

    static string? UserId(ClaimsPrincipal u) => u.FindFirst("sub")?.Value ?? u.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    static async Task<IResult> Get(ClaimsPrincipal user, DirectoryDbContext db, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == uid, ct);
        return p is null ? Problem(404, ErrorCodes.NotFound, "Not found") : Results.Ok(ContractAvailabilityRules.Settings(p));
    }

    static async Task<IResult> Put(ContractAvailabilitySettings req, ClaimsPrincipal user, DirectoryDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (UserId(user) is not { } uid) return Results.Unauthorized();
        var p = await db.Practitioners.SingleOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) return Problem(404, ErrorCodes.NotFound, "Not found");

        var errors = ContractAvailabilityRules.Validate(req, ContractAvailabilityRules.TodayWarsaw(time));
        if (errors.Count > 0) return Problem(422, ErrorCodes.ValidationFailed, "Validation failed", errors);

        ContractAvailabilityRules.Apply(p, req);
        p.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ContractAvailabilityRules.Settings(p));
    }
}

/// <summary>The rules of contract availability, with no I/O. The bounds are owner constants (CONTRACTS section 9 to be updated, ADR proposal 0013 section 6).</summary>
public static class ContractAvailabilityRules
{
    public const int MinHoursPerWeek = 1, MaxHoursPerWeek = 60, MaxMonthsAhead = 24;
    /// <summary>Indicative hourly rate in grosz: 20 zł to 1000 zł.</summary>
    public const long MinRateMinor = 2_000, MaxRateMinor = 100_000;

    static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    public static DateOnly TodayWarsaw(TimeProvider time) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), Warsaw).DateTime);

    public static Dictionary<string, string[]> Validate(ContractAvailabilitySettings s, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Enum.IsDefined(s.Status)) { errors["status"] = ["not_available, available_now or available_from"]; return errors; }
        if (s.Status == ContractStatus.NotAvailable) return errors; // the other fields are ignored and cleared

        if (s.HoursPerWeek is not { } h || h is < MinHoursPerWeek or > MaxHoursPerWeek)
            errors["hoursPerWeek"] = [$"{MinHoursPerWeek} to {MaxHoursPerWeek} hours a week"];
        if (s.Status == ContractStatus.AvailableFrom)
        {
            if (s.AvailableFrom is not { } from) errors["availableFrom"] = ["required for available_from"];
            else if (from <= today) errors["availableFrom"] = ["a date after today; use available_now for today"];
            else if (from > today.AddMonths(MaxMonthsAhead)) errors["availableFrom"] = [$"at most {MaxMonthsAhead} months ahead"];
        }
        if (s.RateMinor is { } r && r is < MinRateMinor or > MaxRateMinor)
            errors["rateMinor"] = [$"{MinRateMinor} to {MaxRateMinor} grosz per hour"];
        return errors;
    }

    /// <summary>Stores an already validated request. "Not available" clears the rest so no stale hours or rate survive a switch off and on.</summary>
    public static void Apply(Practitioner p, ContractAvailabilitySettings s)
    {
        p.ContractStatus = s.Status;
        if (s.Status == ContractStatus.NotAvailable)
        {
            p.ContractAvailableFrom = null; p.ContractHoursPerWeek = null; p.ContractRateMinor = null;
            return;
        }
        p.ContractAvailableFrom = s.Status == ContractStatus.AvailableFrom ? s.AvailableFrom : null;
        p.ContractHoursPerWeek = (short?)s.HoursPerWeek;
        p.ContractRateMinor = s.RateMinor;
    }

    public static ContractAvailabilitySettings Settings(Practitioner p) =>
        new(p.ContractStatus, p.ContractAvailableFrom, p.ContractHoursPerWeek, p.ContractRateMinor);

    /// <summary>The public shape: absent when not available; <c>AvailableFrom</c> null when the date is not in the future.</summary>
    public static ContractAvailabilityDto? Public(Practitioner p, DateOnly today)
    {
        if (p.ContractStatus == ContractStatus.NotAvailable) return null;
        var from = p.ContractStatus == ContractStatus.AvailableFrom && p.ContractAvailableFrom is { } d && d > today ? d : (DateOnly?)null;
        return new ContractAvailabilityDto(from, p.ContractHoursPerWeek ?? 0, p.ContractRateMinor is { } r ? new MoneyDto(r, "PLN") : null);
    }

    public static bool Accepts(Practitioner p) => p.Status == PractitionerStatus.Approved && p.ContractStatus != ContractStatus.NotAvailable;
}
