using System.Net;
using System.Text.Json;
using Gabinety.Contracts.Common;
using Gabinety.Contracts.Directory;
using Gabinety.Directory.Tests.Offerings;

namespace Gabinety.Directory.Tests.Search;

[CollectionDefinition("search", DisableParallelization = true)]
public sealed class SearchCollection : ICollectionFixture<SearchFactory>;

[Collection("search")]
public class SearchListTests(SearchFactory f)
{
    [Fact]
    public async Task Only_Approved_practitioners_with_an_active_offering_are_listed()
    {
        await f.Reset();
        var ok = await f.Approved();
        foreach (var s in new[] { PractitionerStatus.Draft, PractitionerStatus.Submitted, PractitionerStatus.Rejected, PractitionerStatus.Suspended })
            await f.Seed(s, null, new Spec());
        await f.Seed(PractitionerStatus.Approved, null, new Spec(Active: false));   // only an inactive offering
        await f.Seed(PractitionerStatus.Approved);                                   // no offering at all
        Assert.Equal([ok.Id], await Sk.Ids(f.Anon()));
    }

    [Fact]
    public async Task Order_is_ranking_score_then_approved_at_then_id() =>
        await SearchScenarios.Ties_are_ordered_by_score_then_approved_at_then_id(f, SearchScenarios.DotNetOrder);

    [Fact] public Task Each_filter_alone_and_combined_on_the_same_offering() => SearchScenarios.Filters_must_all_hold_on_the_same_offering(f);
    [Fact] public Task Tech_filter() => SearchScenarios.Tech_filter_returns_exactly_the_rows_whose_offering_has_that_tech(f);
    [Fact] public Task Q_is_case_insensitive_and_wildcard_safe() => SearchScenarios.Q_matches_name_or_headline_case_insensitively_and_treats_wildcards_literally(f);
    [Fact] public Task Paging_30_rows_with_ties() => SearchScenarios.Paging_30_rows_with_ties_visits_every_row_once_in_ranking_order(f, SearchScenarios.DotNetOrder);

    [Fact]
    public async Task Unknown_but_well_formed_slugs_match_nobody_and_are_not_errors()
    {
        await f.Reset();
        await f.Approved();
        var c = f.Anon();
        Assert.Empty(await Sk.Ids(c, "?problem=no-such-problem"));
        Assert.Empty(await Sk.Ids(c, "?tech=no-such-tech&problem=code-review"));
    }

    [Theory]
    [InlineData("duration=45", "duration")]
    [InlineData("duration=abc", "duration")]
    [InlineData("maxPriceMinor=-1", "maxPriceMinor")]
    [InlineData("maxPriceMinor=abc", "maxPriceMinor")]
    [InlineData("maxPriceMinor=1.5", "maxPriceMinor")]
    [InlineData("limit=0", "limit")]
    [InlineData("limit=-3", "limit")]
    [InlineData("limit=abc", "limit")]
    [InlineData("problem=Bad_Slug", "problem")]
    [InlineData("tech=a--b", "tech")]
    [InlineData("tech=-a", "tech")]
    [InlineData("cursor=not-a-cursor", "cursor")]
    [InlineData("cursor=MXwyfDM", "cursor")]           // base64url of "1|2|3": decodes, but is not a cursor
    [InlineData("cursor=", "cursor")]
    [InlineData("problem=a&problem=b", "problem")]
    public async Task Bad_parameters_are_400_validation_failed_with_the_field_named(string query, string field)
    {
        var r = await f.Anon().GetAsync("/api/v1/practitioners?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var p = await Kit.ProblemOf(r);
        Assert.Equal(ErrorCodes.ValidationFailed, p.Code);
        Assert.Contains(field, p.Errors!.Keys);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Q_of_60_characters_is_accepted_and_61_is_400()
    {
        await f.Reset();
        var c = f.Anon();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/practitioners?q=" + new string('a', 60))).StatusCode);
        var r = await c.GetAsync("/api/v1/practitioners?q=" + new string('a', 61));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("q", (await Kit.ProblemOf(r)).Errors!.Keys);
    }

    [Fact]
    public async Task Limit_above_24_is_clamped_to_24()
    {
        await f.Reset();
        for (var i = 0; i < 26; i++) await f.Approved(p => p.RankingScore = 40 + i);
        var page = await Sk.Get(f.Anon(), "?limit=25");
        Assert.Equal(24, page.Items.Count);
        Assert.NotNull(page.NextCursor);
        Assert.Equal(26, (await Sk.Walk(f.Anon(), 100)).Ids.Count);
    }

    [Fact]
    public async Task A_malformed_cursor_is_not_treated_as_the_first_page()
    {
        await f.Reset();
        await f.Approved();
        Assert.Equal(HttpStatusCode.BadRequest, (await f.Anon().GetAsync("/api/v1/practitioners?cursor=%%%")).StatusCode);
    }

    [Fact]
    public async Task Rating_is_absent_below_three_reviews_and_has_one_decimal_from_three()
    {
        await f.Reset();
        var two = await f.Approved(p => { p.RatingCount = 2; p.RatingAvg = 5m; p.RankingScore = 90; });
        var three = await f.Approved(p => { p.RatingCount = 3; p.RatingAvg = 4.25m; p.RankingScore = 80; });
        var many = await f.Approved(p => { p.RatingCount = 40; p.RatingAvg = 4.34m; p.RankingScore = 70; });
        var items = (await Sk.Get(f.Anon())).Items;
        Assert.Null(items[0].Rating); Assert.True(items[0].IsNew); Assert.Equal(two.Id, items[0].Id);
        Assert.Equal(new RatingDto(4.3, 3), items[1].Rating); Assert.False(items[1].IsNew); Assert.Equal(three.Id, items[1].Id);
        Assert.Equal(new RatingDto(4.3, 40), items[2].Rating); Assert.Equal(many.Id, items[2].Id);
        var raw = await f.Anon().GetStringAsync("/api/v1/practitioners");
        using var doc = JsonDocument.Parse(raw);
        Assert.False(doc.RootElement.GetProperty("items")[0].TryGetProperty("rating", out _)); // omitted, not null
    }

    [Fact]
    public async Task FromPrice_is_the_lowest_active_offering_and_respects_the_filters()
    {
        await f.Reset();
        var p = await f.Approved(null,
            new Spec("code-review", ["dotnet"], 30, 20000), new Spec("audyt-kodu", ["csharp"], 60, 9000),
            new Spec("code-review", ["dotnet"], 30, 1000, Active: false), new Spec("integracja-ai", ["llm"], 30, 15000));
        var c = f.Anon();
        Assert.Equal(new MoneyDto(9000, "PLN"), (await Sk.Get(c)).Items.Single().FromPrice);
        Assert.Equal(20000, (await Sk.Get(c, "?problem=code-review")).Items.Single().FromPrice.AmountMinor);
        Assert.Equal(15000, (await Sk.Get(c, "?tech=llm")).Items.Single().FromPrice.AmountMinor);
        Assert.Equal(20000, (await Sk.Get(c, "?duration=30&maxPriceMinor=20000&problem=code-review")).Items.Single().FromPrice.AmountMinor);
        var card = (await Sk.Get(c, "?problem=code-review")).Items.Single();
        Assert.Equal(["audyt-kodu", "code-review", "integracja-ai"], card.ProblemSlugs);   // unfiltered, distinct, sorted, active only
        Assert.Equal([30, 60], card.Durations);
        Assert.Equal(p.Id, card.Id);
    }

    [Fact]
    public async Task Badges_follow_the_profile_state_in_a_fixed_order()
    {
        await f.Reset();
        await f.Approved(p => { p.RankingScore = 90; });
        await f.Approved(p => { p.RankingScore = 80; p.GithubLinked = true; p.GithubLogin = "demo-dev"; });
        await f.Approved(p => { p.RankingScore = 70; p.NipValid = true; p.Nip = "5260250995"; });
        await f.Approved(p => { p.RankingScore = 60; p.GithubLinked = true; p.GithubLogin = "demo-dev2"; p.NipValid = true; });
        var items = (await Sk.Get(f.Anon())).Items;
        Assert.Equal([Badge.Email, Badge.Approved], items[0].Badges);
        Assert.Equal([Badge.Email, Badge.Github, Badge.Approved], items[1].Badges);
        Assert.Equal([Badge.Email, Badge.Nip, Badge.Approved], items[2].Badges);
        Assert.Equal([Badge.Email, Badge.Github, Badge.Nip, Badge.Approved], items[3].Badges);
        Assert.Equal("demo-dev", items[1].GithubLogin);
    }

    [Fact]
    public async Task Wire_format_is_public_camelCase_json_with_lowercase_badges_and_cache_headers()
    {
        await f.Reset();
        await f.Approved(p => p.GithubLinked = true);
        var r = await f.Anon().GetAsync("/api/v1/practitioners");     // no Authorization header at all
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=60", r.Headers.CacheControl?.ToString());
        var raw = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var item = doc.RootElement.GetProperty("items")[0];
        Assert.Equal(["email", "github", "approved"], item.GetProperty("badges").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(10000, item.GetProperty("fromPrice").GetProperty("amountMinor").GetInt64());
        Assert.Equal("PLN", item.GetProperty("fromPrice").GetProperty("currency").GetString());
        Assert.True(item.TryGetProperty("displayName", out _) && item.TryGetProperty("problemSlugs", out _) && item.TryGetProperty("isNew", out _));
        Assert.False(doc.RootElement.TryGetProperty("nextCursor", out _));   // omitted on the last page
    }

    [Fact]
    public async Task The_next_cursor_is_present_only_when_more_rows_follow()
    {
        await f.Reset();
        for (var i = 0; i < 3; i++) await f.Approved();
        Assert.NotNull((await Sk.Get(f.Anon(), "?limit=2")).NextCursor);
        Assert.Null((await Sk.Get(f.Anon(), "?limit=3")).NextCursor);
        Assert.Null((await Sk.Get(f.Anon(), "?limit=24")).NextCursor);
    }
}
