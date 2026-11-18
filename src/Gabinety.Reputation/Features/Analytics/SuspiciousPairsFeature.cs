using System.Text.RegularExpressions;
using Gabinety.Contracts.Booking;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Analytics;

/// <summary>
/// <c>GET /api/v1/admin/suspicious-pairs?limit=&amp;cursor=</c> (T-10.03, SPEC 11), <c>Admin</c> policy. The administrator's list of practitioner-client pairs with MORE
/// THAN TWO paid bookings and ONLY five-star reviews: Booking supplies the pairs with their counts (internal endpoint), this feature joins them with its reviews.
/// <list type="bullet">
/// <item>"Booking" means a PAID one: a payment <c>Succeeded</c> or <c>PartiallyRefunded</c> (defined and tested in Booking's pair counts); a fully refunded or never-paid one does not count.</item>
/// <item>A review counts while it is live (<c>Published</c> or <c>Held</c>); a <c>Rejected</c> one neither lists a pair nor clears it. A pair with no review is not listed.</item>
/// <item>Pages are Booking's pages, in its order; the cursor is Booking's, passed through (charset checked). One request reads up to <see cref="MaxScanPages"/> Booking pages and
/// stops after the first page that has a match, so a response has at most <c>limit</c> items (hard cap <see cref="MaxLimit"/>), possibly none with a <c>nextCursor</c> to go on.</item>
/// <item>Privacy: ids, Booking's pair key and two counts. Review text is never selected from the database, so it can be in no response and no log.</item>
/// </list>
/// </summary>
public sealed partial class SuspiciousPairsFeature : IServiceFeature
{
    public const int MinBookings = 3, DefaultLimit = 20, MaxLimit = 50, MaxScanPages = 5, FiveStars = 5;

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,512}$")] private static partial Regex CursorShape();

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/admin/suspicious-pairs", ListAsync).RequireAuthorization("Admin");

    static async Task<IResult> ListAsync(int? limit, string? cursor, ReputationDbContext db, IPairCountsClient booking, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (limit is < 1) errors["limit"] = ["The limit is at least 1."];
        if (cursor is not null && !CursorShape().IsMatch(cursor)) errors["cursor"] = ["The cursor is not valid."];
        if (errors.Count > 0) return Problem(400, ErrorCodes.ValidationFailed, "Validation failed", errors);

        var take = Math.Min(limit ?? DefaultLimit, MaxLimit);
        var items = new List<SuspiciousPairDto>();
        string? next = cursor;
        try
        {
            for (var scanned = 0; scanned < MaxScanPages; scanned++)
            {
                var page = await booking.GetAsync(MinBookings, next, take, ct);
                next = page.NextCursor;
                items.AddRange(await MatchAsync(page.Items.Take(take).Where(p => p.Bookings >= MinBookings).ToList(), db, ct));
                if (items.Count > 0 || next is null) break;
            }
        }
        catch (BookingUnavailableForAnalyticsException)
        {
            return Problem(503, ErrorCodes.DependencyUnavailable, "Dependency unavailable");
        }
        return Results.Json(new Page<SuspiciousPairDto>(items, next));
    }

    /// <summary>The pairs whose live reviews exist and are all five stars. Reads rating and ids only.</summary>
    static async Task<List<SuspiciousPairDto>> MatchAsync(List<PairCountDto> pairs, ReputationDbContext db, CancellationToken ct)
    {
        if (pairs.Count == 0) return [];
        var clients = pairs.Select(p => p.ClientUserId).Distinct().ToList();
        var practitioners = pairs.Select(p => p.PractitionerId).Distinct().ToList();
        var rows = await db.Reviews.AsNoTracking()
            .Where(r => r.Status != ReviewStatus.Rejected && clients.Contains(r.ClientUserId) && practitioners.Contains(r.PractitionerId))
            .Select(r => new { r.ClientUserId, r.PractitionerId, r.Rating })
            .ToListAsync(ct);
        var byPair = rows.GroupBy(r => (r.ClientUserId, r.PractitionerId)).ToDictionary(g => g.Key, g => (Count: g.Count(), AllFive: g.All(r => r.Rating == FiveStars)));
        return [.. pairs
            .Where(p => byPair.TryGetValue((p.ClientUserId, p.PractitionerId), out var r) && r.AllFive)
            .Select(p => new SuspiciousPairDto(p.PairKey, p.ClientUserId, p.PractitionerId, p.Bookings, byPair[(p.ClientUserId, p.PractitionerId)].Count))];
    }

    static IResult Problem(int status, string code, string title, Dictionary<string, string[]>? errors = null) =>
        Results.Json(ApiProblem.Of(code, title, status, null, errors), statusCode: status, contentType: "application/problem+json");
}
