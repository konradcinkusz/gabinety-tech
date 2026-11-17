using System.Globalization;
using System.Text;
using Gabinety.Booking.Features.Payments;
using Gabinety.Booking.Persistence;
using Gabinety.Contracts.Common;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Booking.Features.Admin.Ledger;

/// <summary>
/// <c>GET /api/v1/admin/ledger.csv?from=yyyy-MM-dd&amp;to=yyyy-MM-dd</c> (CONTRACTS 4.3; SPEC 5 rule 2), behind the <c>Admin</c> policy.
/// <list type="bullet">
/// <item>Columns <c>booking_id,kind,amount_minor,currency,created_at</c>; nothing else (no entry id, no pair key, no user id). The ledger has no currency column, so
/// <c>currency</c> is <c>PLN</c> for every row (the only currency of the platform).</item>
/// <item>"Dates" are UTC days: the range is <c>[from 00:00:00Z, to + 1 day 00:00:00Z)</c>, both days inclusive; at most 366 days counting both ends.</item>
/// <item>Rows are ordered by <c>(created_at, id)</c> and read in keyset pages of <see cref="PageSize"/>, so a year of rows is never in memory at once. The entry id is
/// read for the order only and never written.</item>
/// <item>The body is UTF-8 without BOM, rows end with CRLF. Nothing is logged: no rows, no ids.</item>
/// </list>
/// </summary>
public sealed class AdminLedgerFeature : IServiceFeature
{
    public const int PageSize = 1000, MaxDays = 366;
    public const string Currency = "PLN", Header = "booking_id,kind,amount_minor,currency,created_at";
    static readonly string[] Columns = Header.Split(',');

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/admin/ledger.csv", Export).RequireAuthorization("Admin");

    static IResult Export(string? from, string? to, HttpContext http, BookingDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        var f = ParseDay(from, "from", errors);
        var t = ParseDay(to, "to", errors);
        if (f is { } a && t is { } b)
        {
            if (b < a) errors["to"] = ["The end date is not before the start date."];
            else if (b.DayNumber - a.DayNumber + 1 > MaxDays) errors["to"] = [$"The range is at most {MaxDays} days."];
        }
        if (errors.Count > 0) return Problem(errors);

        var start = new DateTimeOffset(f!.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(t!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); // exclusive
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"ledger-{Day(f.Value)}-{Day(t.Value)}.csv\"";
        return Results.Stream(s => WriteAsync(s, db, start, end, http.RequestAborted), "text/csv; charset=utf-8");
    }

    static async Task WriteAsync(Stream body, BookingDbContext db, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        await using var text = new StreamWriter(body, new UTF8Encoding(false), 16 * 1024, leaveOpen: true);
        var csv = new CsvWriter(text);
        await csv.WriteRowAsync(Columns);

        DateTimeOffset? lastAt = null;
        var lastId = Guid.Empty;
        while (true)
        {
            var q = db.LedgerEntries.AsNoTracking().Where(e => e.CreatedAt >= start && e.CreatedAt < end);
            if (lastAt is { } at) q = q.Where(e => e.CreatedAt > at || (e.CreatedAt == at && e.Id.CompareTo(lastId) > 0));
            var page = await q.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).Take(PageSize)
                .Select(e => new { e.Id, e.BookingId, e.Kind, e.AmountMinor, e.CreatedAt }).ToListAsync(ct);
            foreach (var e in page)
                await csv.WriteRowAsync(e.BookingId.ToString("D"), e.Kind.ToString(), e.AmountMinor.ToString(CultureInfo.InvariantCulture), Currency,
                    e.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
            if (page.Count < PageSize) break;
            (lastAt, lastId) = (page[^1].CreatedAt, page[^1].Id);
        }
        await text.FlushAsync(ct);
    }

    static DateOnly? ParseDay(string? value, string name, Dictionary<string, string[]> errors)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        errors[name] = [$"The {name} date is required, as yyyy-MM-dd."];
        return null;
    }

    static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static IResult Problem(Dictionary<string, string[]> errors) =>
        Results.Json(ApiProblem.Of(ErrorCodes.ValidationFailed, "Validation failed", 400, null, errors), statusCode: 400, contentType: "application/problem+json");
}
