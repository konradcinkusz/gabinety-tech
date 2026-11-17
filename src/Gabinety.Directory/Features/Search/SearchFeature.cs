using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Offerings;
using Offering = Gabinety.Directory.Features.Offerings.Offering;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Gabinety.Ranking;
using Gabinety.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Features.Search;

/// <summary>
/// Public directory reads (CONTRACTS 3.2, no authentication): the ranked list, the profile and the sitemap. Listed = Approved with at least one
/// active offering matching the filters. Every query builds entities, orders, takes and executes; records are made in memory afterwards (P4).
/// The global 60/min limiter applies; the 120/min of CONTRACTS 10.2 for public reads is not configured per route yet.
/// </summary>
public sealed class SearchFeature : IServiceFeature
{
    public const int IndexableFrom = 3;

    public void Add(IHostApplicationBuilder builder) { }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        var g = endpoints.MapGroup("/api/v1").AllowAnonymous();
        g.MapGet("practitioners", List);
        g.MapGet("practitioners/{slug}", Profile);
        g.MapGet("sitemap", Sitemap);
    }

    // ---- list

    static async Task<IResult> List(HttpContext ctx, DirectoryDbContext db, CancellationToken ct)
    {
        var s = SearchQuery.Parse(ctx.Request.Query);
        if (s.Error is { } e) return PublicJson.Invalid(ctx, e.Field, e.Message);

        var offerings = s.Filter.Apply(db.Offerings);
        var q = db.Practitioners.AsNoTracking()
            .Where(p => p.Status == PractitionerStatus.Approved && offerings.Any(o => o.PractitionerId == p.Id));
        if (s.Q is { } text)
        {
            var needle = text.ToLowerInvariant(); // Contains escapes % and _ itself on both providers: the user's text is always literal
            q = q.Where(p => p.DisplayName.ToLower().Contains(needle) || p.Headline.ToLower().Contains(needle));
        }
        if (s.After is { } a)
            q = q.Where(p => p.RankingScore < a.Score
                || (p.RankingScore == a.Score && p.ApprovedAt > a.ApprovedAt)
                || (p.RankingScore == a.Score && p.ApprovedAt == a.ApprovedAt && p.Id.CompareTo(a.Id) > 0));

        // Order BEFORE any projection; the total order is (score desc, approved_at asc, id asc).
        var rows = await q.OrderByDescending(p => p.RankingScore).ThenBy(p => p.ApprovedAt).ThenBy(p => p.Id).Take(s.Limit + 1).ToListAsync(ct);
        var page = rows.Take(s.Limit).ToList();

        var ids = page.Select(p => p.Id).ToList();
        var all = ids.Count == 0 ? [] : await db.Offerings.AsNoTracking().Where(o => ids.Contains(o.PractitionerId) && o.IsActive).ToListAsync(ct); // one query for the page
        var byPractitioner = all.ToLookup(o => o.PractitionerId);

        var items = page.Select(p => Card(p, byPractitioner[p.Id].ToList(), s.Filter)).ToList();
        var next = rows.Count > s.Limit ? new Cursor(page[^1].RankingScore, page[^1].ApprovedAt ?? DateTimeOffset.MinValue, page[^1].Id).Encode() : null;
        return PublicJson.Respond(ctx, new Page<PractitionerCard>(items, next), "public, max-age=60");
    }

    static PractitionerCard Card(Practitioner p, List<Offering> active, OfferingFilter? filter)
    {
        var pool = filter is null ? active : active.Where(filter.Matches).ToList();
        var rating = Display.Rating(p.RatingCount, (double)p.RatingAvg);
        var badges = new List<Badge> { Badge.Email };
        if (p.GithubLinked) badges.Add(Badge.Github);
        if (p.NipValid) badges.Add(Badge.Nip);
        badges.Add(Badge.Approved);
        return new PractitionerCard(p.Id, p.Slug, p.DisplayName, p.Headline, p.GithubLogin, badges,
            rating.IsNew ? null : new RatingDto(rating.Average!.Value, rating.Count), rating.IsNew,
            new MoneyDto(pool.Count == 0 ? 0 : pool.Min(o => o.PriceMinor), "PLN"),
            active.Select(o => o.ProblemSlug).Distinct().Order(StringComparer.Ordinal).ToList(),
            active.Select(o => (int)o.DurationMinutes).Distinct().Order().ToList());
    }

    // ---- profile

    static async Task<IResult> Profile(string slug, HttpContext ctx, DirectoryDbContext db, CancellationToken ct)
    {
        var p = await db.Practitioners.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == PractitionerStatus.Approved, ct);
        if (p is null) return PublicJson.NotFound(ctx);
        var active = await db.Offerings.AsNoTracking().Where(o => o.PractitionerId == p.Id && o.IsActive).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct);
        var offerings = active.Select(o => new Gabinety.Contracts.Directory.Offering(o.Id, o.Title, o.Description, o.DurationMinutes, new MoneyDto(o.PriceMinor, o.Currency),
            o.ProblemSlug, o.TechSlugs.ToList())).ToList();
        var body = ProfileWire.Of(new PractitionerProfile(Card(p, active, null), p.Bio, p.PortfolioUrl, offerings, p.CompletedCount, p.ApprovedAt ?? p.CreatedAt));
        return PublicJson.Respond(ctx, body, "public, max-age=300, stale-while-revalidate=3600", etag: true);
    }

    // ---- sitemap

    static async Task<IResult> Sitemap(HttpContext ctx, DirectoryDbContext db, CancellationToken ct)
    {
        var approved = db.Practitioners.AsNoTracking().Where(p => p.Status == PractitionerStatus.Approved);
        var pages = (await approved.Select(p => new { p.Slug, p.UpdatedAt }).ToListAsync(ct))
            .OrderBy(p => p.Slug, StringComparer.Ordinal).Select(p => new SitemapPractitioner(p.Slug, p.UpdatedAt)).ToList(); // slugs are a-z0-9-: ordinal is the stable order

        var active = await db.Categories.AsNoTracking().Where(c => c.IsActive).Select(c => new { c.Slug, c.Kind }).ToListAsync(ct);
        var problems = active.Where(c => c.Kind == CategoryKind.Problem).Select(c => c.Slug).ToHashSet();
        var techs = active.Where(c => c.Kind == CategoryKind.Tech).Select(c => c.Slug).ToHashSet();
        var rows = await db.Offerings.AsNoTracking()
            .Where(o => o.IsActive && approved.Any(p => p.Id == o.PractitionerId))
            .Select(o => new { o.PractitionerId, o.ProblemSlug, o.TechSlugs }).ToListAsync(ct);

        var counts = new Dictionary<string, HashSet<Guid>> { ["/programisci"] = rows.Select(r => r.PractitionerId).ToHashSet() };
        void Add(string path, Guid id) { if (!counts.TryGetValue(path, out var set)) counts[path] = set = []; set.Add(id); }
        foreach (var r in rows.Where(r => problems.Contains(r.ProblemSlug)))
        {
            Add($"/programisci/{r.ProblemSlug}", r.PractitionerId);
            foreach (var t in r.TechSlugs.Where(techs.Contains)) Add($"/programisci/{r.ProblemSlug}/{t}", r.PractitionerId);
        }
        var categories = counts.OrderBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new SitemapCategory(c.Key, c.Value.Count, c.Value.Count >= IndexableFrom)).ToList();
        return PublicJson.Respond(ctx, new SitemapDto(pages, categories), "public, max-age=60");
    }
}

/// <summary>The wire shape of `PractitionerProfile` (CONTRACTS 3.2): the card's fields and the extras side by side (flat), not the card nested under `card`.</summary>
internal sealed record ProfileWire(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin,
    IReadOnlyList<Badge> Badges, RatingDto? Rating, bool IsNew, MoneyDto FromPrice,
    IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations,
    string Bio, string? PortfolioUrl, IReadOnlyList<Gabinety.Contracts.Directory.Offering> Offerings, int CompletedCount,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset MemberSince)
{
    public static ProfileWire Of(PractitionerProfile p) => new(p.Card.Id, p.Card.Slug, p.Card.DisplayName, p.Card.Headline, p.Card.GithubLogin,
        p.Card.Badges, p.Card.Rating, p.Card.IsNew, p.Card.FromPrice, p.Card.ProblemSlugs, p.Card.Durations,
        p.Bio, p.PortfolioUrl, p.Offerings, p.CompletedCount, p.MemberSince);
}
