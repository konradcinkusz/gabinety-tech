using System.Security.Claims;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Availability;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;

namespace Gabinety.Booking.Features.Payouts;

/// <summary>
/// <c>GET /api/v1/me/earnings?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</c> (CONTRACTS 4.2, T-11.15), practitioner only: the caller's own money in a Warsaw-day range, the current month by default.
/// A caller who is not a practitioner is <c>403</c> (before the dates are looked at); the practitioner id comes from Directory by the token's user, never from the request, so no other
/// practitioner's rows can be asked for. Money is <c>{ amountMinor, currency }</c> in grosz (<c>PLN</c>, the platform's only currency), never a decimal. The figures are <see cref="EarningsCalculator"/>'s.
/// </summary>
public sealed class EarningsFeature : IServiceFeature
{
    const string Currency = "PLN";

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/me/earnings", GetAsync).RequireAuthorization("Authenticated");

    static async Task<IResult> GetAsync(string? from, string? to, ClaimsPrincipal user, PractitionerResolver who, BookingDbContext db, TimeProvider time, CancellationToken ct)
    {
        var userId = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId is null) return Results.Unauthorized();

        Guid? practitioner;
        try { practitioner = await who.ResolveAsync(userId, ct); }
        catch (DirectoryUnavailableException)
        {
            return Problem(ApiProblem.Of(ErrorCodes.DependencyUnavailable, "Dependency unavailable", 503, "A required service is temporarily unavailable."));
        }
        if (practitioner is not { } id) return Problem(ApiProblem.Of(ErrorCodes.Forbidden, "Forbidden", 403, "The caller is not a practitioner."));

        if (!EarningsPeriod.TryResolve(from, to, time.GetUtcNow(), out var period, out var errors))
            return Problem(ApiProblem.Of(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors));

        var t = await EarningsQueries.TotalsAsync(db, id, period, ct);
        return Results.Ok(new EarningsDto(period.From, period.To, Money(t.PaidOut), Money(t.Pending), Money(t.Disputed), Money(t.Fees), t.Count));
    }

    static MoneyDto Money(long minor) => new(minor, Currency);

    static IResult Problem(ApiProblem p) => Results.Json(p, statusCode: p.Status, contentType: "application/problem+json");
}
