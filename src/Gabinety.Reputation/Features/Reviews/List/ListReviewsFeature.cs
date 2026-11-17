using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Reputation;
using Gabinety.Reputation.Persistence;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Reputation.Features.Reviews.List;

/// <summary>
/// GET /api/v1/practitioners/{practitionerId}/reviews (CONTRACTS 5.2), public. Published only, newest first by (published_at, id); the reviewer is
/// not part of the answer at all (the page labels every reviewer "Zweryfikowany klient"). The cursor is opaque: the last item's published_at ticks and id.
/// </summary>
public sealed class ListReviewsFeature : IServiceFeature
{
    public const int MaxLimit = 20;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/practitioners/{practitionerId:guid}/reviews", HandleAsync).AllowAnonymous();

    static async Task<IResult> HandleAsync(Guid practitionerId, string? cursor, int? limit, ReputationDbContext db, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? MaxLimit, 1, MaxLimit);
        var q = db.Reviews.AsNoTracking().Where(x => x.PractitionerId == practitionerId && x.Status == ReviewStatus.Published);
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out var at, out var id))
                return Results.Json(ApiProblem.Of(ErrorCodes.ValidationFailed, "Validation failed", 400, errors: new Dictionary<string, string[]> { ["cursor"] = ["cursor is not valid"] }),
                    statusCode: 400, contentType: "application/problem+json");
            q = q.Where(x => x.PublishedAt < at || (x.PublishedAt == at && x.Id.CompareTo(id) < 0));
        }
        var rows = await q.OrderByDescending(x => x.PublishedAt).ThenByDescending(x => x.Id).Take(take + 1)
            .Select(x => new { x.Id, x.Rating, x.Text, x.CreatedAt, x.PublishedAt, x.ReplyText, x.RepliedAt }).ToListAsync(ct);
        var more = rows.Count > take;
        if (more) rows.RemoveAt(take);
        var items = rows.Select(r => new ReviewDto(r.Id, r.Rating, r.Text, r.CreatedAt,
            r.ReplyText is null || r.RepliedAt is null ? null : new ReplyDto(r.ReplyText, r.RepliedAt.Value))).ToList();
        var next = more ? Encode(rows[^1].PublishedAt!.Value, rows[^1].Id) : null;
        return Results.Json(new Page<ReviewDto>(items, next));
    }

    static string Encode(DateTimeOffset at, Guid id) =>
        Base64Url.EncodeToString(Encoding.ASCII.GetBytes($"{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}.{id:N}"));

    static bool TryDecode(string cursor, out DateTimeOffset at, out Guid id)
    {
        at = default; id = default;
        try
        {
            var parts = Encoding.ASCII.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out id) || ticks > DateTime.MaxValue.Ticks) return false;
            at = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
