using Gabinety.Contracts.Directory;

namespace Gabinety.Directory.Tests.Search;

/// <summary>
/// Scenarios that must hold on every provider: the InMemory tests and the PostgreSQL tests both run them. The id comparer is the provider's order of
/// `id asc`: .NET orders a Guid by its fields, PostgreSQL orders a uuid by its bytes (the "N" text).
/// </summary>
public static class SearchScenarios
{
    public static readonly IComparer<Guid> DotNetOrder = Comparer<Guid>.Default;
    public static readonly IComparer<Guid> PostgresOrder = Comparer<Guid>.Create((a, b) => string.CompareOrdinal(a.ToString("N"), b.ToString("N")));

    public static async Task Paging_30_rows_with_ties_visits_every_row_once_in_ranking_order(SearchFactory f, IComparer<Guid> idOrder)
    {
        await f.Reset();
        var rows = new List<Practitioner_>();
        for (var i = 0; i < 30; i++)
        {
            var score = 90m - i % 5 * 5;                      // six rows per score
            var approved = SearchFactory.Epoch.AddHours(i / 3); // groups of three share the instant too
            var p = await f.Approved(x => { x.RankingScore = score; x.ApprovedAt = approved; });
            rows.Add(new(p.Id, score, approved));
        }
        var expected = rows.OrderByDescending(r => r.Score).ThenBy(r => r.ApprovedAt).ThenBy(r => r.Id, idOrder).Select(r => r.Id).ToList();
        var c = f.Anon();

        foreach (var limit in new[] { 7, 1, 12, 24 })
        {
            var (ids, pages, last) = await Sk.Walk(c, limit);
            Assert.Equal(expected, ids);                       // no gap, no duplicate, same order
            Assert.Equal((30 + limit - 1) / limit, pages);
            Assert.Null(last);
        }
        var first = await Sk.Get(c, "?limit=24");
        Assert.Equal(24, first.Items.Count);
        Assert.NotNull(first.NextCursor);                      // 6 rows follow
        Assert.Equal(12, (await Sk.Get(c)).Items.Count);       // default limit
    }

    public static async Task Tech_filter_returns_exactly_the_rows_whose_offering_has_that_tech(SearchFactory f)
    {
        await f.Reset();
        var both = await f.Approved(null, new Spec(Techs: ["dotnet", "csharp"]));
        var one = await f.Approved(null, new Spec(Techs: ["csharp"]));
        var other = await f.Approved(null, new Spec(Techs: ["postgresql"]));
        await f.Approved(null, new Spec(Techs: []));
        var inactive = await f.Approved(null, new Spec(Techs: ["csharp"], Active: false), new Spec(Techs: ["python"]));
        var c = f.Anon();

        Assert.Equivalent(new[] { both.Id, one.Id }, await Sk.Ids(c, "?tech=csharp"));
        Assert.Equivalent(new[] { both.Id }, await Sk.Ids(c, "?tech=dotnet"));
        Assert.Equivalent(new[] { other.Id }, await Sk.Ids(c, "?tech=postgresql"));
        Assert.Equivalent(new[] { inactive.Id }, await Sk.Ids(c, "?tech=python"));  // only its active offering counts
        Assert.Empty(await Sk.Ids(c, "?tech=kubernetes"));
        Assert.Equal(5, (await Sk.Ids(c, "?limit=24")).Count);
    }

    public static async Task Filters_must_all_hold_on_the_same_offering(SearchFactory f)
    {
        await f.Reset();
        // A: code-review for 200 zl and audyt-kodu for 50 zl, so "problem=code-review AND max 100 zl" is satisfied by two different offerings.
        var a = await f.Approved(null, new Spec("code-review", ["dotnet"], 30, 20000), new Spec("audyt-kodu", ["csharp"], 60, 5000));
        var b = await f.Approved(null, new Spec("code-review", ["dotnet"], 30, 8000));
        var c = f.Anon();

        Assert.Equivalent(new[] { b.Id }, await Sk.Ids(c, "?problem=code-review&maxPriceMinor=10000"));
        Assert.Equivalent(new[] { a.Id }, await Sk.Ids(c, "?problem=audyt-kodu&maxPriceMinor=10000"));
        Assert.Empty(await Sk.Ids(c, "?problem=audyt-kodu&tech=dotnet"));              // dotnet is on the other offering
        Assert.Equivalent(new[] { a.Id }, await Sk.Ids(c, "?problem=audyt-kodu&tech=csharp&duration=60&maxPriceMinor=5000"));
        Assert.Empty(await Sk.Ids(c, "?problem=audyt-kodu&duration=30"));
        Assert.Equivalent(new[] { a.Id, b.Id }, await Sk.Ids(c, "?duration=30"));
        Assert.Equivalent(new[] { a.Id }, await Sk.Ids(c, "?duration=60"));
        Assert.Equivalent(new[] { b.Id }, await Sk.Ids(c, "?maxPriceMinor=8000&problem=code-review"));
    }

    public static async Task Ties_are_ordered_by_score_then_approved_at_then_id(SearchFactory f, IComparer<Guid> idOrder)
    {
        await f.Reset();
        var t = SearchFactory.Epoch;
        var top = await f.Approved(p => { p.RankingScore = 80; p.ApprovedAt = t.AddDays(9); });
        var early = await f.Approved(p => { p.RankingScore = 70; p.ApprovedAt = t; });
        var same = new List<Guid>();
        for (var i = 0; i < 4; i++) same.Add((await f.Approved(p => { p.RankingScore = 70; p.ApprovedAt = t.AddDays(1); })).Id);
        var late = await f.Approved(p => { p.RankingScore = 70; p.ApprovedAt = t.AddDays(2); });
        var low = await f.Approved(p => { p.RankingScore = 69.99m; p.ApprovedAt = t; });

        var expected = new List<Guid> { top.Id, early.Id };
        expected.AddRange(same.Order(idOrder));
        expected.AddRange([late.Id, low.Id]);
        Assert.Equal(expected, await Sk.Ids(f.Anon(), "?limit=24"));
        Assert.Equal(expected, (await Sk.Walk(f.Anon(), 2)).Ids);          // and the cursor keeps that order across the tied rows
    }

    public static async Task Q_matches_name_or_headline_case_insensitively_and_treats_wildcards_literally(SearchFactory f)
    {
        await f.Reset();
        var anna = await f.Approved(p => p.DisplayName = "Anna 100% Dev");
        var bob = await f.Approved(p => p.DisplayName = "Bob_Builder");
        var carol = await f.Approved(p => { p.DisplayName = "Carol Nowak"; p.Headline = "Expert in Rust and C++"; });
        var back = await f.Approved(p => p.DisplayName = @"Dan\Slash");
        var c = f.Anon();

        Assert.Equivalent(new[] { anna.Id }, await Sk.Ids(c, "?q=anna"));
        Assert.Equivalent(new[] { anna.Id }, await Sk.Ids(c, "?q=ANNA"));
        Assert.Equivalent(new[] { anna.Id }, await Sk.Ids(c, "?q=" + Uri.EscapeDataString("100%")));
        Assert.Equivalent(new[] { anna.Id }, await Sk.Ids(c, "?q=" + Uri.EscapeDataString("%")));     // a literal %, not "everything"
        Assert.Equivalent(new[] { bob.Id }, await Sk.Ids(c, "?q=" + Uri.EscapeDataString("_")));      // a literal _, not "any character"
        Assert.Equivalent(new[] { bob.Id }, await Sk.Ids(c, "?q=" + Uri.EscapeDataString("b_b")));
        Assert.Empty(await Sk.Ids(c, "?q=" + Uri.EscapeDataString("b__b")));
        Assert.Equivalent(new[] { back.Id }, await Sk.Ids(c, "?q=" + Uri.EscapeDataString(@"\")));
        Assert.Equivalent(new[] { carol.Id }, await Sk.Ids(c, "?q=rust"));                            // headline
        Assert.Equivalent(new[] { carol.Id }, await Sk.Ids(c, "?q=" + Uri.EscapeDataString("  RUST AND  ".Trim())));
        Assert.Equal(4, (await Sk.Ids(c, "?q=" + Uri.EscapeDataString("   "))).Count);                // blank: no filter
        Assert.Equal(4, (await Sk.Ids(c, "?q=")).Count);
    }

    sealed record Practitioner_(Guid Id, decimal Score, DateTimeOffset ApprovedAt);
}
