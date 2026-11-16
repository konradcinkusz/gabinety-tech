using System.Net;
using System.Net.Http.Json;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Features.Profiles;
using Gabinety.Directory.Features.Ranking;
using Gabinety.Directory.Tests.Offerings;
using Gabinety.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Gabinety.Directory.Tests.Search;

[Collection("search")]
public class SitemapTests(SearchFactory f)
{
    async Task<SitemapDto> Map(HttpResponseMessage? r = null)
    {
        r ??= await f.Anon().GetAsync("/api/v1/sitemap");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("public, max-age=60", r.Headers.CacheControl?.ToString());
        return (await r.Content.ReadFromJsonAsync<SitemapDto>(Sk.J))!;
    }

    [Fact]
    public async Task The_root_category_is_always_present_even_when_nobody_is_listed()
    {
        await f.Reset();
        var m = await Map();
        Assert.Empty(m.Practitioners);
        Assert.Equal([new SitemapCategory("/programisci", 0, false)], m.Categories);
    }

    [Fact]
    public async Task A_category_is_indexable_from_three_listed_practitioners_not_from_two()
    {
        await f.Reset();
        for (var i = 0; i < 2; i++) await f.Approved(null, new Spec("code-review", ["dotnet"]));
        var two = await Map();
        Assert.Contains(new SitemapCategory("/programisci/code-review", 2, false), two.Categories);
        Assert.Contains(new SitemapCategory("/programisci/code-review/dotnet", 2, false), two.Categories);
        await f.Approved(null, new Spec("code-review", ["dotnet", "csharp"]));
        var three = await Map();
        Assert.Contains(new SitemapCategory("/programisci", 3, true), three.Categories);
        Assert.Contains(new SitemapCategory("/programisci/code-review", 3, true), three.Categories);
        Assert.Contains(new SitemapCategory("/programisci/code-review/dotnet", 3, true), three.Categories);
        Assert.Contains(new SitemapCategory("/programisci/code-review/csharp", 1, false), three.Categories);
    }

    [Fact]
    public async Task Only_listed_practitioners_count_each_counts_once_and_empty_pairs_are_omitted()
    {
        await f.Reset();
        await f.Approved(null, new Spec("code-review", ["dotnet"]), new Spec("code-review", ["dotnet"], 60));   // two offerings, one practitioner
        await f.Seed(PractitionerStatus.Suspended, null, new Spec("code-review", ["dotnet"]));
        await f.Seed(PractitionerStatus.Approved, null, new Spec("code-review", ["dotnet"], Active: false));    // approved, not listed
        var m = await Map();
        Assert.Equal(2, m.Practitioners.Count);                                                                   // every Approved one is in the sitemap
        Assert.Equal(["/programisci", "/programisci/code-review", "/programisci/code-review/dotnet"], m.Categories.Select(c => c.Path));
        Assert.All(m.Categories, c => Assert.Equal(1, c.PractitionerCount));
        Assert.DoesNotContain(m.Categories, c => c.Path.Contains("audyt-kodu"));
    }

    [Fact]
    public async Task Practitioners_are_ordered_by_slug_with_updated_at_and_categories_by_path()
    {
        await f.Reset();
        var b = await f.Approved(p => { p.Slug = "demo-bbb"; p.UpdatedAt = SearchFactory.Epoch.AddDays(2); }, new Spec("integracja-ai", ["llm"]));
        var a = await f.Approved(p => { p.Slug = "demo-aaa"; p.UpdatedAt = SearchFactory.Epoch.AddDays(1); }, new Spec("audyt-kodu", ["csharp"]));
        await f.Seed(PractitionerStatus.Draft, p => p.Slug = "demo-aab");
        var m = await Map();
        Assert.Equal([new SitemapPractitioner("demo-aaa", a.UpdatedAt), new SitemapPractitioner("demo-bbb", b.UpdatedAt)], m.Practitioners);
        Assert.Equal(m.Categories.Select(c => c.Path).Order(StringComparer.Ordinal), m.Categories.Select(c => c.Path));
        Assert.Equal("/programisci", m.Categories[0].Path);
    }
}

[Collection("search")]
public class RankingRulesTests(SearchFactory f)
{
    [Fact]
    public async Task Rules_equal_the_library_constants_and_the_default_effective_date()
    {
        var r = await f.Anon().GetAsync("/api/v1/ranking-rules");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("public, max-age=3600", r.Headers.CacheControl?.ToString());
        var d = (await r.Content.ReadFromJsonAsync<RankingRulesDto>(Sk.J))!;
        Assert.Equal(RankingRules.Version, d.Version);
        Assert.Equal(RankingRules.WeightRating, d.Weights.Rating);
        Assert.Equal(RankingRules.WeightReliability, d.Weights.Reliability);
        Assert.Equal(RankingRules.WeightAvailability, d.Weights.Availability);
        Assert.Equal(RankingRules.WeightCompleteness, d.Weights.Completeness);
        Assert.Equal(RankingRules.M, d.Constants.M);
        Assert.Equal(RankingRules.Mu, d.Constants.Mu);
        Assert.Equal(RankingRules.MinReviewsToShowRating, d.Constants.MinReviewsToShowRating);
        Assert.Equal(RankingRules.SlotsForFullAvailability, d.Constants.SlotsForFullAvailability);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero), d.EffectiveFrom);
        Assert.Contains("\"effectiveFrom\":\"2026-11-16T00:00:00Z\"", await f.Anon().GetStringAsync("/api/v1/ranking-rules"));
    }

    [Fact]
    public async Task The_effective_date_comes_from_configuration()
    {
        using var g = f.WithWebHostBuilder(b => b.UseSetting("Ranking:EffectiveFrom", "2027-01-05T10:00:00Z"));
        var d = (await g.CreateClient().GetFromJsonAsync<RankingRulesDto>("/api/v1/ranking-rules", Sk.J))!;
        Assert.Equal(new DateTimeOffset(2027, 1, 5, 10, 0, 0, TimeSpan.Zero), d.EffectiveFrom);
        Assert.Equal(RankingRules.Version, d.Version);
    }
}

public class RankingRecalculatorTests
{
    static Practitioner Row(PractitionerStatus status) => new() { Status = status, ProfileCompleteness = 1m, OpenSlots14d = 10, RankingScore = 1m, RankingVersion = "old" };

    [Fact]
    public void The_contract_vector_scores_82_95_and_stamps_the_version()
    {
        var p = Row(PractitionerStatus.Approved);      // n=0, r=0, c=0, x=0, s=0, p=1, k=10
        Assert.True(RankingRecalculator.Recompute(p));
        Assert.Equal(82.95m, p.RankingScore);
        Assert.Equal(RankingRules.Version, p.RankingVersion);
    }

    [Fact]
    public void A_changed_input_changes_the_stored_score()
    {
        var p = Row(PractitionerStatus.Approved);
        RankingRecalculator.Recompute(p);
        var before = p.RankingScore;
        p.ProfileCompleteness = 0.5m; p.RatingCount = 10; p.RatingAvg = 5m;
        RankingRecalculator.Recompute(p);
        Assert.NotEqual(before, p.RankingScore);
        Assert.Equal((decimal)Ranker.Score(new RankingInput(10, 5, 0, 0, 0, 0.5, 10)), p.RankingScore);
    }

    [Theory]
    [InlineData(PractitionerStatus.Draft)] [InlineData(PractitionerStatus.Submitted)] [InlineData(PractitionerStatus.Rejected)] [InlineData(PractitionerStatus.Suspended)]
    public void A_row_that_is_not_Approved_is_not_scored(PractitionerStatus status)
    {
        var p = Row(status);
        Assert.False(RankingRecalculator.Recompute(p));
        Assert.Equal(1m, p.RankingScore);
        Assert.Equal("old", p.RankingVersion);
    }
}

[Collection("search")]
public class RankingRecomputeCallSiteTests(SearchFactory f)
{
    [Fact]
    public async Task Admin_approve_stores_the_contract_score()
    {
        await f.Reset();
        var p = await f.Seed(PractitionerStatus.Submitted, x => { x.ProfileCompleteness = 1m; x.OpenSlots14d = 10; x.RankingScore = 0; x.RankingVersion = ""; x.ApprovedAt = null; });
        Assert.Equal(HttpStatusCode.NoContent, (await f.As("demo-admin", "Admin").PostAsync($"/api/v1/admin/practitioners/{p.Id}/approve", null)).StatusCode);
        var row = await f.Load(p.Id);
        Assert.Equal(82.95m, row.RankingScore);
        Assert.Equal(RankingRules.Version, row.RankingVersion);
    }

    [Fact]
    public async Task Creating_an_offering_on_an_Approved_profile_changes_its_completeness_and_so_its_stored_score()
    {
        await f.Reset();
        var uid = Kit.Id();
        var p = await f.Seed(PractitionerStatus.Approved, x => { x.UserId = uid; x.ProfileCompleteness = 0m; x.RankingScore = 0m; x.RankingVersion = ""; });
        var r = await f.As(uid).PostAsJsonAsync(Kit.Url, Kit.Valid());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var row = await f.Load(p.Id);
        Assert.True(row.ProfileCompleteness > 0m);                       // the active offering is a completeness component
        Assert.Equal((decimal)Ranker.Score(new RankingInput(0, 0, 0, 0, 0, (double)row.ProfileCompleteness, 0)), row.RankingScore);
        Assert.True(row.RankingScore > 0m);
        Assert.Equal(RankingRules.Version, row.RankingVersion);
    }

    [Fact]
    public async Task Editing_a_draft_profile_does_not_score_it()
    {
        await f.Reset();
        var uid = Kit.Id();
        var p = await f.Seed(PractitionerStatus.Draft, x => { x.UserId = uid; x.RankingScore = 0m; x.RankingVersion = ""; });
        var r = await f.As(uid).PostAsJsonAsync(Kit.Url, Kit.Valid());
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var row = await f.Load(p.Id);
        Assert.Equal(0m, row.RankingScore);
        Assert.Equal("", row.RankingVersion);
    }
}
