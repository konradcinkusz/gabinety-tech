using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Offerings;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Search;
using Gabinety.Directory.Features.Taxonomy;
using Gabinety.Directory.Persistence;
using Gabinety.Directory.Tests.Offerings;
using Gabinety.Directory.Tests.Taxonomy;
using Gabinety.Directory.Tests.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gabinety.Directory.Tests.Search;

/// <summary>One offering to seed. Defaults: active, 30 minutes, 100 zloty, code-review, .NET.</summary>
public sealed record Spec(string Problem = "code-review", string[]? Techs = null, int Duration = 30, long Price = 10000, bool Active = true);

/// <summary>Directory host for the public search tests (shares the in-memory database with other test classes, so these run in one serial collection and reset it first).</summary>
public class SearchFactory : VerificationFactory
{
    public static readonly DateTimeOffset Epoch = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
    int _n;

    public HttpClient Anon() => CreateClient();

    /// <summary>Waits for the host's own taxonomy seed, makes sure every seeded category is active, and empties practitioners and offerings.</summary>
    public async Task Reset()
    {
        await Kit.WaitForSeed(this);
        await Db(async db =>
        {
            db.Offerings.RemoveRange(db.Offerings);
            db.VerificationEvents.RemoveRange(db.VerificationEvents);
            db.Practitioners.RemoveRange(db.Practitioners);
            await db.SaveChangesAsync();
            var known = Expected.Problems.Select(x => (x.Slug, x.NamePl, CategoryKind.Problem)).Concat(Expected.Techs.Select(x => (x.Slug, x.NamePl, CategoryKind.Tech))).ToList();
            var rows = await db.Categories.ToListAsync();
            foreach (var (slug, name, kind) in known)
            {
                if (rows.FirstOrDefault(c => c.Slug == slug) is { } c) c.IsActive = true;
                else db.Categories.Add(new Category { Slug = slug, Kind = kind, NamePl = name, Sort = 1, IsActive = true });
            }
            await db.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>Adds a practitioner with its offerings. Status Approved by default; ApprovedAt counts up from Epoch unless set.</summary>
    public Task<Practitioner> Seed(PractitionerStatus status = PractitionerStatus.Approved, Action<Practitioner>? configure = null, params Spec[] offerings) => Db(async db =>
    {
        var n = Guid.NewGuid().ToString("n");
        var k = Interlocked.Increment(ref _n);
        var p = new Practitioner
        {
            Id = Guid.NewGuid(), UserId = "demo-" + n, Slug = "demo-" + n[..16], DisplayName = "Demo " + n[..8], Headline = "Senior .NET reviewer",
            Bio = "Demo bio", Status = status, CreatedAt = Epoch, UpdatedAt = Epoch.AddMinutes(k),
            ApprovedAt = status == PractitionerStatus.Approved ? Epoch.AddMinutes(k) : null, RankingScore = 50, RankingVersion = "2026-11-v1",
        };
        configure?.Invoke(p);
        db.Practitioners.Add(p);
        foreach (var (s, i) in offerings.Select((s, i) => (s, i)))
            db.Offerings.Add(new Gabinety.Directory.Features.Offerings.Offering
            {
                Id = Guid.NewGuid(), PractitionerId = p.Id, Title = "Demo offering", Description = "d", DurationMinutes = (short)s.Duration, PriceMinor = s.Price,
                Currency = "PLN", ProblemSlug = s.Problem, TechSlugs = s.Techs ?? ["dotnet"], IsActive = s.Active, CreatedAt = Epoch.AddMinutes(k).AddSeconds(i),
            });
        await db.SaveChangesAsync();
        return p;
    });

    public Task<Practitioner> Approved(Action<Practitioner>? configure = null, params Spec[] offerings) =>
        Seed(PractitionerStatus.Approved, configure, offerings.Length == 0 ? [new Spec()] : offerings);
}

public static class Sk
{
    public static readonly JsonSerializerOptions J = PublicJson.Options;

    public static async Task<Page<PractitionerCard>> Get(HttpClient c, string query = "")
    {
        var r = await c.GetAsync("/api/v1/practitioners" + query);
        Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<Page<PractitionerCard>>(J))!;
    }

    public static async Task<List<Guid>> Ids(HttpClient c, string query = "") => (await Get(c, query)).Items.Select(i => i.Id).ToList();

    /// <summary>Follows nextCursor until the last page; returns every id in the order served.</summary>
    public static async Task<(List<Guid> Ids, int Pages, string? LastCursor)> Walk(HttpClient c, int limit, string extra = "")
    {
        var ids = new List<Guid>(); string? cursor = null; var pages = 0;
        do
        {
            var page = await Get(c, $"?limit={limit}{extra}" + (cursor is null ? "" : "&cursor=" + cursor));
            ids.AddRange(page.Items.Select(i => i.Id)); pages++; cursor = page.NextCursor;
            Assert.InRange(pages, 1, 100);
        } while (cursor is not null);
        return (ids, pages, cursor);
    }
}
