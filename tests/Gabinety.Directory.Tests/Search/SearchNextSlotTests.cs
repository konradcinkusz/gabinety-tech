using System.Net;
using System.Text.Json;
using Gabinety.Directory.Features.Search;

namespace Gabinety.Directory.Tests.Search;

/// <summary>`sort=next_slot` (T-11.03): shared by the in-memory host tests and the PostgreSQL ones.</summary>
public static class NextSlotScenarios
{
    sealed record Row(Guid Id, DateTimeOffset? Slot, decimal Score, DateTimeOffset Approved);

    static async Task<(List<Guid> Ids, Dictionary<Guid, string?> Slots)> Walk(HttpClient c, int limit, string extra)
    {
        var ids = new List<Guid>(); var slots = new Dictionary<Guid, string?>(); string? cursor = null; var pages = 0;
        do
        {
            var r = await c.GetAsync($"/api/v1/practitioners?limit={limit}{extra}" + (cursor is null ? "" : "&cursor=" + cursor));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var id = item.GetProperty("id").GetGuid();
                ids.Add(id);
                slots[id] = item.TryGetProperty("nextFreeSlotUtc", out var v) ? v.GetString() : null;
            }
            cursor = doc.RootElement.TryGetProperty("nextCursor", out var n) ? n.GetString() : null;
            Assert.InRange(++pages, 1, 100);
        } while (cursor is not null);
        return (ids, slots);
    }

    /// <summary>24 practitioners: a quarter without a slot, the rest on six instants (ties), scores and approval instants tied in groups.</summary>
    public static async Task Next_slot_sort_puts_practitioners_without_a_slot_last(SearchFactory f, IComparer<Guid> idOrder)
    {
        await f.Reset();
        var rows = new List<Row>();
        for (var i = 0; i < 24; i++)
        {
            DateTimeOffset? slot = i % 4 == 0 ? null : SearchFactory.Epoch.AddDays(1).AddMinutes(i % 6 * 30);
            var score = 90m - i % 3 * 5;
            var approved = SearchFactory.Epoch.AddHours(i / 4);
            var p = await f.Approved(x => { x.NextFreeSlotUtc = slot; x.RankingScore = score; x.ApprovedAt = approved; });
            rows.Add(new Row(p.Id, slot, score, approved));
        }
        var expected = rows.OrderBy(r => r.Slot is null).ThenBy(r => r.Slot).ThenByDescending(r => r.Score).ThenBy(r => r.Approved).ThenBy(r => r.Id, idOrder).Select(r => r.Id).ToList();
        var c = f.Anon();

        foreach (var limit in new[] { 5, 1, 7, 24 })
        {
            var (ids, slots) = await Walk(c, limit, "&sort=next_slot");
            Assert.Equal(expected, ids);                       // every practitioner once, earliest first, none last, ties in ranking order
            foreach (var r in rows) Assert.Equal(r.Slot is null ? null : r.Slot.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), slots[r.Id]);
        }
        Assert.Equal(6, expected.Take(18).Select(id => rows.Single(r => r.Id == id).Slot).Distinct().Count());
        Assert.All(expected.Skip(18), id => Assert.Null(rows.Single(r => r.Id == id).Slot));

        // the other sorts are untouched by the new column
        var rank = rows.OrderByDescending(r => r.Score).ThenBy(r => r.Approved).ThenBy(r => r.Id, idOrder).Select(r => r.Id).ToList();
        Assert.Equal(rank, (await Walk(c, 5, "&sort=rank")).Ids);
    }

    /// <summary>A filter leaves the same order, and everyone without a slot is still last.</summary>
    public static async Task Next_slot_sort_works_with_a_filter_and_only_unscheduled_practitioners(SearchFactory f)
    {
        await f.Reset();
        var late = await f.Approved(x => x.NextFreeSlotUtc = SearchFactory.Epoch.AddDays(5), new Spec("code-review"), new Spec("audyt-kodu"));
        var soon = await f.Approved(x => x.NextFreeSlotUtc = SearchFactory.Epoch.AddDays(1), new Spec("code-review"));
        var none = await f.Approved(x => x.RankingScore = 99, new Spec("code-review"));
        var other = await f.Approved(x => x.NextFreeSlotUtc = SearchFactory.Epoch.AddHours(1), new Spec("audyt-kodu"));
        var c = f.Anon();
        Assert.Equal([soon.Id, late.Id, none.Id], (await Walk(c, 12, "&sort=next_slot&problem=code-review")).Ids);
        Assert.Equal([other.Id, late.Id], (await Walk(c, 12, "&sort=next_slot&problem=audyt-kodu")).Ids);

        await f.Reset();
        var a = await f.Approved(x => x.RankingScore = 10);
        var b = await f.Approved(x => x.RankingScore = 20);
        Assert.Equal([b.Id, a.Id], (await Walk(c, 1, "&sort=next_slot")).Ids);    // nobody has a slot: the ranking order
    }
}

[Collection("search")]
public class SearchNextSlotTests(SearchFactory f)
{
    [Fact] public Task Next_slot_sort_puts_practitioners_without_a_slot_last_in_memory() => NextSlotScenarios.Next_slot_sort_puts_practitioners_without_a_slot_last(f, SearchScenarios.DotNetOrder);
    [Fact] public Task Next_slot_sort_works_with_a_filter_and_only_unscheduled_practitioners() => NextSlotScenarios.Next_slot_sort_works_with_a_filter_and_only_unscheduled_practitioners(f);

    [Fact]
    public async Task The_profile_carries_the_next_free_slot_only_when_set()
    {
        await f.Reset();
        var withSlot = await f.Approved(x => x.NextFreeSlotUtc = SearchFactory.Epoch.AddDays(1));
        var without = await f.Approved();
        var c = f.Anon();
        using var a = JsonDocument.Parse(await c.GetStringAsync($"/api/v1/practitioners/{withSlot.Slug}"));
        Assert.Equal("2026-11-02T08:00:00Z", a.RootElement.GetProperty("nextFreeSlotUtc").GetString());
        using var b = JsonDocument.Parse(await c.GetStringAsync($"/api/v1/practitioners/{without.Slug}"));
        Assert.False(b.RootElement.TryGetProperty("nextFreeSlotUtc", out _));
    }

    [Fact]
    public async Task A_next_slot_cursor_is_only_valid_for_the_next_slot_sort()
    {
        await f.Reset();
        for (var i = 0; i < 3; i++) await f.Approved(x => x.NextFreeSlotUtc = SearchFactory.Epoch.AddDays(i + 1), new Spec(Price: 5000 + i * 1000));
        var c = f.Anon();
        var next = (await Sk.Get(c, "?limit=1&sort=next_slot")).NextCursor!;
        var rank = (await Sk.Get(c, "?limit=1")).NextCursor!;
        var price = (await Sk.Get(c, "?limit=1&sort=price_asc")).NextCursor!;

        foreach (var query in new[] { $"cursor={next}", $"sort=rank&cursor={next}", $"sort=price_asc&cursor={next}", $"sort=next_slot&cursor={rank}", $"sort=next_slot&cursor={price}" })
        {
            var r = await c.GetAsync("/api/v1/practitioners?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            Assert.Equal("validation_failed", doc.RootElement.GetProperty("code").GetString());
            Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty("cursor", out _));
        }
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/v1/practitioners?sort=next_slot&cursor={next}")).StatusCode);
    }

    [Fact]
    public void Next_slot_cursors_round_trip_with_and_without_a_slot()
    {
        var id = Guid.NewGuid(); var at = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero); var slot = at.AddDays(2);
        foreach (var cursor in new[] { new Cursor(SearchSort.NextSlot, 0, 71.5m, at, id, slot), new Cursor(SearchSort.NextSlot, 0, 71.5m, at, id, null) })
        {
            Assert.True(Cursor.TryDecode(cursor.Encode(), out var back));
            Assert.Equal(cursor, back);
        }
    }

    [Theory]
    [InlineData("next_slot|abc|71|1|00000000000000000000000000000001")]
    [InlineData("next_slot|-1|71|1|00000000000000000000000000000001")]
    [InlineData("next_slot|9999999999999999999|71|1|00000000000000000000000000000001")]
    [InlineData("next_slot|none|71|1")]
    public void A_malformed_next_slot_cursor_does_not_decode(string plain) =>
        Assert.False(Cursor.TryDecode(System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.ASCII.GetBytes(plain)), out _));
}

[Trait("tier", "integration")]
public class SearchNextSlotIntegrationTests(PgSearchFixture pg) : IClassFixture<PgSearchFixture>
{
    [Fact] public Task Next_slot_sort_puts_practitioners_without_a_slot_last() => NextSlotScenarios.Next_slot_sort_puts_practitioners_without_a_slot_last(pg.Factory, SearchScenarios.PostgresOrder);
    [Fact] public Task Next_slot_sort_works_with_a_filter_and_only_unscheduled_practitioners() => NextSlotScenarios.Next_slot_sort_works_with_a_filter_and_only_unscheduled_practitioners(pg.Factory);

    [Fact]
    public async Task Migration_adds_a_nullable_timestamptz_next_free_slot_column()
    {
        await pg.Factory.Reset();   // the host has applied every migration by now
        await using var c = new Npgsql.NpgsqlConnection(pg.ConnectionString);
        await c.OpenAsync();
        async Task<object?> One(string sql) { await using var cmd = new Npgsql.NpgsqlCommand(sql, c); return await cmd.ExecuteScalarAsync(); }
        Assert.Equal(1L, await One("""select count(*) from "__EFMigrationsHistory" where "MigrationId" = '20261122120000_T-11.03_NextFreeSlot'"""));
        Assert.Equal("timestamp with time zone|YES", await One("""select data_type || '|' || is_nullable from information_schema.columns where table_name = 'practitioners' and column_name = 'next_free_slot_utc'"""));
    }
}
