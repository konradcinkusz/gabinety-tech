using System.Net;
using System.Text.Json;
using Gabinety.Directory.Features.Search;

namespace Gabinety.Directory.Tests.Search;

/// <summary>The orders and the card's `techSlugs` shared by the in-memory host tests and the PostgreSQL ones (T-11.02).</summary>
public static class SortScenarios
{
    /// <summary>30 practitioners, six per price (ties), scores and approval instants tied in groups; some with a cheaper inactive offering that must not count.</summary>
    public static async Task Price_sort_pages_return_each_practitioner_once(SearchFactory f, IComparer<Guid> idOrder)
    {
        await f.Reset();
        var rows = new List<(Guid Id, long Price, decimal Score, DateTimeOffset Approved)>();
        for (var i = 0; i < 30; i++)
        {
            var price = 5000L + i % 5 * 2500;                      // 5000 .. 15000, six practitioners per price
            var score = 90m - i % 3 * 5;
            var approved = SearchFactory.Epoch.AddHours(i / 4);
            var specs = new List<Spec> { new(Price: price) };
            if (i % 2 == 0) specs.Add(new Spec(Price: price + 40000));            // a dearer active offering: the lowest still counts
            if (i % 3 == 0) specs.Add(new Spec(Price: 5000, Active: false));     // a cheaper inactive one: ignored
            var p = await f.Approved(x => { x.RankingScore = score; x.ApprovedAt = approved; }, [.. specs]);
            rows.Add((p.Id, price, score, approved));
        }
        var rank = rows.OrderByDescending(r => r.Score).ThenBy(r => r.Approved).ThenBy(r => r.Id, idOrder).Select(r => r.Id).ToList();
        var asc = rows.OrderBy(r => r.Price).ThenByDescending(r => r.Score).ThenBy(r => r.Approved).ThenBy(r => r.Id, idOrder).Select(r => r.Id).ToList();
        var desc = rows.OrderByDescending(r => r.Price).ThenByDescending(r => r.Score).ThenBy(r => r.Approved).ThenBy(r => r.Id, idOrder).Select(r => r.Id).ToList();
        var c = f.Anon();

        foreach (var limit in new[] { 7, 1, 12, 24 })
        {
            var (a, pages, last) = await Sk.Walk(c, limit, "&sort=price_asc");
            Assert.Equal(asc, a);                                  // every practitioner once, cheapest first, ties in ranking order
            Assert.Equal((30 + limit - 1) / limit, pages);
            Assert.Null(last);
            Assert.Equal(desc, (await Sk.Walk(c, limit, "&sort=price_desc")).Ids);
        }
        var first = await Sk.Get(c, "?sort=price_asc&limit=24");
        Assert.Equal(5000, first.Items[0].FromPrice.AmountMinor);
        Assert.Equal(first.Items.Select(i => i.FromPrice.AmountMinor).Order(), first.Items.Select(i => i.FromPrice.AmountMinor));
        Assert.Equal(rank, (await Sk.Walk(c, 5, "&sort=rank")).Ids);   // the explicit rank and the default are the ranking order
        Assert.Equal(rank.Take(24), await Sk.Ids(c, "?limit=24"));
    }

    /// <summary>The price a filter leaves is the price the sort uses (the same one `fromPrice` shows).</summary>
    public static async Task Price_sort_follows_the_filtered_from_price(SearchFactory f)
    {
        await f.Reset();
        var a = await f.Approved(null, new Spec("code-review", Price: 6000), new Spec("audyt-kodu", Price: 30000));
        var b = await f.Approved(null, new Spec("code-review", Price: 12000), new Spec("audyt-kodu", Price: 9000));
        var c = f.Anon();
        Assert.Equal([a.Id, b.Id], await Sk.Ids(c, "?sort=price_asc"));
        Assert.Equal([b.Id, a.Id], await Sk.Ids(c, "?sort=price_asc&problem=audyt-kodu"));
        Assert.Equal([a.Id, b.Id], await Sk.Ids(c, "?sort=price_desc&problem=audyt-kodu"));
    }

    public static async Task Tech_slugs_are_the_sorted_union_over_active_offerings_at_most_six(SearchFactory f)
    {
        await f.Reset();
        var p = await f.Approved(null,
            new Spec(Techs: ["rust", "dotnet", "csharp"]), new Spec(Techs: ["python", "csharp", "go", "kotlin", "swift"]),
            new Spec(Techs: ["aaa-inactive"], Active: false));
        var few = await f.Approved(null, new Spec(Techs: ["zeta", "alpha"]));
        var none = await f.Approved(null, new Spec(Techs: []));
        var r = await f.Anon().GetAsync("/api/v1/practitioners?limit=24");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        string[] Techs(Guid id) => doc.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id)
            .GetProperty("techSlugs").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(["csharp", "dotnet", "go", "kotlin", "python", "rust"], Techs(p.Id));   // swift is the seventh: cut after sorting
        Assert.Equal(["alpha", "zeta"], Techs(few.Id));
        Assert.Empty(Techs(none.Id));
    }
}

[Collection("search")]
public class SearchSortTests(SearchFactory f)
{
    [Fact] public Task Price_sort_pages_return_each_practitioner_once() => SortScenarios.Price_sort_pages_return_each_practitioner_once(f, SearchScenarios.DotNetOrder);
    [Fact] public Task Price_sort_uses_the_price_of_the_matching_offerings() => SortScenarios.Price_sort_follows_the_filtered_from_price(f);
    [Fact] public Task Tech_slugs_are_sorted_active_only_and_capped() => SortScenarios.Tech_slugs_are_the_sorted_union_over_active_offerings_at_most_six(f);

    [Theory]
    [InlineData("sort=newest")]
    [InlineData("sort=")]
    [InlineData("sort=PRICE_ASC")]
    [InlineData("sort=rank&sort=price_asc")]
    public async Task An_unknown_or_repeated_sort_is_a_validation_failure(string query)
    {
        await f.Reset();
        var r = await f.Anon().GetAsync("/api/v1/practitioners?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
        Assert.True(doc.RootElement.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task A_cursor_of_another_sort_is_a_validation_failure()
    {
        await f.Reset();
        for (var i = 0; i < 3; i++) await f.Approved(null, new Spec(Price: 5000 + i * 1000));
        var c = f.Anon();
        var rank = (await Sk.Get(c, "?limit=1")).NextCursor!;
        var asc = (await Sk.Get(c, "?limit=1&sort=price_asc")).NextCursor!;
        var desc = (await Sk.Get(c, "?limit=1&sort=price_desc")).NextCursor!;

        foreach (var query in new[] { $"sort=price_asc&cursor={rank}", $"cursor={asc}", $"sort=price_desc&cursor={asc}", $"sort=price_asc&cursor={desc}", $"sort=rank&cursor={desc}" })
        {
            var r = await c.GetAsync("/api/v1/practitioners?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("cursor", out _));
        }
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/practitioners?sort=price_asc&cursor={asc}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/practitioners?cursor={rank}")).StatusCode);
    }

    [Fact]
    public void Cursors_round_trip_with_their_sort_and_the_rank_cursor_keeps_its_three_part_form()
    {
        var id = Guid.NewGuid(); var at = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        foreach (var sort in new[] { SearchSort.Rank, SearchSort.PriceAsc, SearchSort.PriceDesc })
        {
            var cursor = new Cursor(sort, sort == SearchSort.Rank ? 0 : 12345, 71.5m, at, id);
            Assert.True(Cursor.TryDecode(cursor.Encode(), out var back));
            Assert.Equal(cursor, back);
        }
        var legacy = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.ASCII.GetBytes($"71.5|{at.UtcTicks}|{id:N}"));
        Assert.Equal(legacy, new Cursor(71.5m, at, id).Encode());
        Assert.True(Cursor.TryDecode(legacy, out var old));
        Assert.Equal(SearchSort.Rank, old!.Sort);
    }

    [Theory]
    [InlineData("rank|1|2|3|4")]
    [InlineData("price_up|1|2|3|4")]
    [InlineData("price_asc|-1|71|1|00000000000000000000000000000001")]
    [InlineData("price_asc|1|71|1")]
    public void A_malformed_sort_cursor_does_not_decode(string plain) =>
        Assert.False(Cursor.TryDecode(System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.ASCII.GetBytes(plain)), out _));
}

[Trait("tier", "integration")]
public class SearchSortIntegrationTests(PgSearchFixture pg) : IClassFixture<PgSearchFixture>
{
    [Fact] public Task Price_sort_pages_return_each_practitioner_once() => SortScenarios.Price_sort_pages_return_each_practitioner_once(pg.Factory, SearchScenarios.PostgresOrder);
    [Fact] public Task Price_sort_uses_the_price_of_the_matching_offerings() => SortScenarios.Price_sort_follows_the_filtered_from_price(pg.Factory);
    [Fact] public Task Tech_slugs_are_sorted_active_only_and_capped() => SortScenarios.Tech_slugs_are_the_sorted_union_over_active_offerings_at_most_six(pg.Factory);
}
