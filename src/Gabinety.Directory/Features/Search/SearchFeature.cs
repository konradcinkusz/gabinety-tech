using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.ContractAvailability;
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

    static async Task<IResult> List(HttpContext ctx, DirectoryDbContext db, TimeProvider time, CancellationToken ct)
    {
        var s = SearchQuery.Parse(ctx.Request.Query);
        if (s.Error is { } e) return PublicJson.Invalid(ctx, e.Field, e.Message);

        var offerings = s.Filter.Apply(db.Offerings);
        var q = db.Practitioners.AsNoTracking()
            .Where(p => p.Status == PractitionerStatus.Approved && offerings.Any(o => o.PractitionerId == p.Id));
        if (s.Contract) q = q.Where(p => p.ContractStatus != ContractStatus.NotAvailable); // ADR proposal 0013: only those who take collaboration proposals
        if (s.Q is { } text)
        {
            var needle = text.ToLowerInvariant(); // Contains escapes % and _ itself on both providers: the user's text is always literal
            q = q.Where(p => p.DisplayName.ToLower().Contains(needle) || p.Headline.ToLower().Contains(needle));
        }
        // The keyset of the ranking order (score desc, approved_at asc, id asc) is the tail of every sort.
        var after = s.After;
        System.Linq.Expressions.Expression<Func<Practitioner, bool>>? afterRank = after is null ? null
            : p => p.RankingScore < after.Score
                || (p.RankingScore == after.Score && p.ApprovedAt > after.ApprovedAt)
                || (p.RankingScore == after.Score && p.ApprovedAt == after.ApprovedAt && p.Id.CompareTo(after.Id) > 0);

        // Order BEFORE any projection (lesson #78): the order is on the entity, or on an anonymous (entity, price) pair, never on a constructed record.
        List<Practitioner> rows; List<long> prices = [];
        if (s.Sort == SearchSort.Rank)
        {
            if (afterRank is not null) q = q.Where(afterRank);
            rows = await q.OrderByDescending(p => p.RankingScore).ThenBy(p => p.ApprovedAt).ThenBy(p => p.Id).Take(s.Limit + 1).ToListAsync(ct);
        }
        else if (s.Sort == SearchSort.NextSlot)
        {
            // Ascending by the next free slot, practitioners without one last, then the ranking order. Ordered on the entity itself.
            if (after is not null)
            {
                var slot = after.Slot;
                q = slot is { } at
                    ? q.Where(p => p.NextFreeSlotUtc == null || p.NextFreeSlotUtc > at || (p.NextFreeSlotUtc == at && (p.RankingScore < after.Score
                        || (p.RankingScore == after.Score && p.ApprovedAt > after.ApprovedAt)
                        || (p.RankingScore == after.Score && p.ApprovedAt == after.ApprovedAt && p.Id.CompareTo(after.Id) > 0))))
                    : q.Where(p => p.NextFreeSlotUtc == null && (p.RankingScore < after.Score
                        || (p.RankingScore == after.Score && p.ApprovedAt > after.ApprovedAt)
                        || (p.RankingScore == after.Score && p.ApprovedAt == after.ApprovedAt && p.Id.CompareTo(after.Id) > 0)));
            }
            rows = await q.OrderBy(p => p.NextFreeSlotUtc == null).ThenBy(p => p.NextFreeSlotUtc)
                .ThenByDescending(p => p.RankingScore).ThenBy(p => p.ApprovedAt).ThenBy(p => p.Id).Take(s.Limit + 1).ToListAsync(ct);
        }
        else
        {
            // The lowest price among the offerings that match the filters, i.e. the `fromPrice` the card shows.
            var priced = q.Select(p => new { P = p, Price = offerings.Where(o => o.PractitionerId == p.Id).Min(o => (long?)o.PriceMinor) ?? 0L });
            var asc = s.Sort == SearchSort.PriceAsc;
            if (after is not null)
                priced = asc
                    ? priced.Where(x => x.Price > after.Price || (x.Price == after.Price && (x.P.RankingScore < after.Score
                        || (x.P.RankingScore == after.Score && x.P.ApprovedAt > after.ApprovedAt)
                        || (x.P.RankingScore == after.Score && x.P.ApprovedAt == after.ApprovedAt && x.P.Id.CompareTo(after.Id) > 0))))
                    : priced.Where(x => x.Price < after.Price || (x.Price == after.Price && (x.P.RankingScore < after.Score
                        || (x.P.RankingScore == after.Score && x.P.ApprovedAt > after.ApprovedAt)
                        || (x.P.RankingScore == after.Score && x.P.ApprovedAt == after.ApprovedAt && x.P.Id.CompareTo(after.Id) > 0))));
            var ordered = asc ? priced.OrderBy(x => x.Price) : priced.OrderByDescending(x => x.Price);
            var withPrice = await ordered.ThenByDescending(x => x.P.RankingScore).ThenBy(x => x.P.ApprovedAt).ThenBy(x => x.P.Id).Take(s.Limit + 1).ToListAsync(ct);
            rows = withPrice.Select(x => x.P).ToList();
            prices = withPrice.Take(s.Limit).Select(x => x.Price).ToList();
        }
        var page = rows.Take(s.Limit).ToList();

        var ids = page.Select(p => p.Id).ToList();
        var all = ids.Count == 0 ? [] : await db.Offerings.AsNoTracking().Where(o => ids.Contains(o.PractitionerId) && o.IsActive).ToListAsync(ct); // one query for the page
        var byPractitioner = all.ToLookup(o => o.PractitionerId);

        var today = ContractAvailabilityRules.TodayWarsaw(time);
        var items = page.Select(p => { var active = byPractitioner[p.Id].ToList(); return CardWire.Of(Card(p, active, s.Filter, today), TechSlugs(active), p.NextFreeSlotUtc); }).ToList();
        var next = rows.Count > s.Limit
            ? new Cursor(s.Sort, prices.Count == 0 ? 0 : prices[^1], page[^1].RankingScore, page[^1].ApprovedAt ?? DateTimeOffset.MinValue, page[^1].Id, s.Sort == SearchSort.NextSlot ? page[^1].NextFreeSlotUtc : null).Encode() : null;
        return PublicJson.Respond(ctx, new Page<CardWire>(items, next), "public, max-age=60");
    }

    public const int MaxTechSlugs = 6;

    /// <summary>The sorted union of `tech_slugs` over the practitioner's active offerings (all of them, not only those matching the filters), at most six (CONTRACTS 3.2).</summary>
    static List<string> TechSlugs(List<Offering> active) =>
        active.Where(o => o.IsActive).SelectMany(o => o.TechSlugs).Distinct().Order(StringComparer.Ordinal).Take(MaxTechSlugs).ToList();

    static PractitionerCard Card(Practitioner p, List<Offering> active, OfferingFilter? filter, DateOnly today)
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
            active.Select(o => (int)o.DurationMinutes).Distinct().Order().ToList(), ContractAvailabilityRules.Public(p, today));
    }

    // ---- profile

    static async Task<IResult> Profile(string slug, HttpContext ctx, DirectoryDbContext db, TimeProvider time, CancellationToken ct)
    {
        var p = await db.Practitioners.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.Status == PractitionerStatus.Approved, ct);
        if (p is null) return PublicJson.NotFound(ctx);
        var active = await db.Offerings.AsNoTracking().Where(o => o.PractitionerId == p.Id && o.IsActive).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).ToListAsync(ct);
        var offerings = active.Select(o => new Gabinety.Contracts.Directory.Offering(o.Id, o.Title, o.Description, o.DurationMinutes, new MoneyDto(o.PriceMinor, o.Currency),
            o.ProblemSlug, o.TechSlugs.ToList())).ToList();
        var body = ProfileWire.Of(new PractitionerProfile(Card(p, active, null, ContractAvailabilityRules.TodayWarsaw(time)), p.Bio, p.PortfolioUrl, offerings, p.CompletedCount, p.ApprovedAt ?? p.CreatedAt), TechSlugs(active), p.NextFreeSlotUtc);
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

/// <summary>
/// The wire shape of `PractitionerCard` including the wave 3 `techSlugs` and `nextFreeSlotUtc` (CONTRACTS 3.2). The shared contract record in `Gabinety.Contracts` does not carry
/// the field yet (that file is outside T-11.02), so the Directory serializes this record, which is the card plus those two (`nextFreeSlotUtc` is omitted when null).
/// </summary>
internal sealed record CardWire(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin,
    IReadOnlyList<Badge> Badges, RatingDto? Rating, bool IsNew, MoneyDto FromPrice,
    IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations, IReadOnlyList<string> TechSlugs, ContractAvailabilityDto? Contract,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? NextFreeSlotUtc)
{
    public static CardWire Of(PractitionerCard c, IReadOnlyList<string> techSlugs, DateTimeOffset? nextFreeSlotUtc) => new(c.Id, c.Slug, c.DisplayName, c.Headline, c.GithubLogin,
        c.Badges, c.Rating, c.IsNew, c.FromPrice, c.ProblemSlugs, c.Durations, techSlugs, c.Contract, nextFreeSlotUtc);
}

/// <summary>The wire shape of `PractitionerProfile` (CONTRACTS 3.2): the card's fields and the extras side by side (flat), not the card nested under `card`.</summary>
internal sealed record ProfileWire(Guid Id, string Slug, string DisplayName, string Headline, string? GithubLogin,
    IReadOnlyList<Badge> Badges, RatingDto? Rating, bool IsNew, MoneyDto FromPrice,
    IReadOnlyList<string> ProblemSlugs, IReadOnlyList<int> Durations, IReadOnlyList<string> TechSlugs, ContractAvailabilityDto? Contract,
    string Bio, string? PortfolioUrl, IReadOnlyList<Gabinety.Contracts.Directory.Offering> Offerings, int CompletedCount,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset MemberSince,
    [property: System.Text.Json.Serialization.JsonConverter(typeof(UtcInstantNullableConverter))] DateTimeOffset? NextFreeSlotUtc)
{
    public static ProfileWire Of(PractitionerProfile p, IReadOnlyList<string> techSlugs, DateTimeOffset? nextFreeSlotUtc) => new(p.Card.Id, p.Card.Slug, p.Card.DisplayName, p.Card.Headline, p.Card.GithubLogin,
        p.Card.Badges, p.Card.Rating, p.Card.IsNew, p.Card.FromPrice, p.Card.ProblemSlugs, p.Card.Durations, techSlugs, p.Card.Contract,
        p.Bio, p.PortfolioUrl, p.Offerings, p.CompletedCount, p.MemberSince, nextFreeSlotUtc);
}
