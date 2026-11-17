using System.Net;
using System.Text.Json;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>GET /api/v1/admin/disputes (CONTRACTS 4.3): open = Disputed, resolved = Resolved, newest first, cursor paging, admin only.</summary>
public class DisputesListTests
{
    static async Task<(List<JsonElement> Items, string? Next)> Page(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var root = await r.Body();
        return ([.. root.GetProperty("items").EnumerateArray()], root.TryGetProperty("nextCursor", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
    }

    /// <summary>Every page of the list. The InMemory database is shared by all tests of the process, so the rows of other tests are in the same newest-first list and a single page can miss this test's rows.</summary>
    static async Task<List<JsonElement>> All(CreateHost h, string query = "")
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        do
        {
            var q = cursor is null ? query : query + (query.Contains('?') ? "&" : "?") + "cursor=" + Uri.EscapeDataString(cursor);
            var (page, next) = await Page(await Adm.List(h, q));
            items.AddRange(page);
            cursor = next;
        } while (cursor is not null);
        return items;
    }

    static string[] Mine(List<JsonElement> items, CreateHost h) =>
        [.. items.Where(i => i.GetProperty("practitionerUserId").GetString() == h.OwnerUserId).Select(i => i.GetProperty("bookingId").GetString()!)];

    [Fact]
    public async Task Open_lists_Disputed_and_resolved_lists_Resolved_and_nothing_else_appears()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var now = h.Clock.GetUtcNow();
        var (open, openClient) = await Adm.Seed(h, kind: "practitioner_no_show", openedAt: now.AddHours(-2));
        var (done, _) = await Adm.Seed(h, BookingStatus.Resolved, resolution: "refund_partial", openedAt: now.AddHours(-9), resolvedAt: now.AddHours(-1));
        foreach (var s in Enum.GetValues<BookingStatus>().Where(s => s is not (BookingStatus.Disputed or BookingStatus.Resolved))) await Adm.Seed(h, s);

        var openItems = await All(h);
        Assert.Equal([open.ToString()], Mine(openItems, h));
        var defaultItems = await All(h, "?status=open");
        Assert.Equal(Mine(openItems, h), Mine(defaultItems, h));
        var resolvedItems = await All(h, "?status=resolved");
        Assert.Equal([done.ToString()], Mine(resolvedItems, h));

        var item = openItems.Single(i => i.GetProperty("bookingId").GetString() == open.ToString());
        Assert.Equal(openClient, item.GetProperty("clientUserId").GetString());
        Assert.Equal(h.OwnerUserId, item.GetProperty("practitionerUserId").GetString());
        Assert.Equal("practitioner_no_show", item.GetProperty("kind").GetString());
        Assert.Equal(Adm.Text, item.GetProperty("text").GetString());
        Assert.Equal(now.AddHours(-2), item.GetProperty("openedAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("resolution").ValueKind);

        var r = resolvedItems.Single(i => i.GetProperty("bookingId").GetString() == done.ToString());
        Assert.Equal("refund_partial", r.GetProperty("resolution").GetString());
        Assert.Equal(now.AddHours(-1), r.GetProperty("resolvedAt").GetDateTimeOffset());
        Assert.Equal(now.AddHours(-9), r.GetProperty("openedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_booking_moves_from_the_open_list_to_the_resolved_list_when_resolved()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        Assert.Contains(id.ToString(), Mine(await All(h), h));
        Assert.Equal(HttpStatusCode.OK, (await Adm.Resolve(h, id, Adm.Body("release"))).StatusCode);
        Assert.DoesNotContain(id.ToString(), Mine(await All(h), h));
        var resolved = (await All(h, "?status=resolved")).Single(i => i.GetProperty("bookingId").GetString() == id.ToString());
        Assert.Equal("release", resolved.GetProperty("resolution").GetString());
        Assert.Equal(Adm.Note, resolved.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Newest_first_with_a_cursor_that_walks_every_item_once()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var now = h.Clock.GetUtcNow();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++) ids.Add((await Adm.Seed(h, openedAt: now.AddMinutes(-10 * i))).Id); // ids[0] is the newest
        ids.Add((await Adm.Seed(h, openedAt: now.AddMinutes(-10))).Id); // ties with ids[1]: the id decides, descending

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var (items, next) = await Page(await Adm.List(h, cursor is null ? "?limit=2" : $"?limit=2&cursor={Uri.EscapeDataString(cursor)}"));
            seen.AddRange(Mine(items, h));
            cursor = next;
            pages++;
        } while (cursor is not null && pages < 5000); // the list is global and the InMemory database is shared by every test of the process: walk all of it (2 rows per page), only this host's owner is counted

        Assert.Equal(6, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.Equal(ids[0].ToString(), seen[0]);
        Assert.Equal(ids[4].ToString(), seen[^1]);
        var tied = new[] { ids[1], ids[5] }.OrderByDescending(x => x).Select(x => x.ToString());
        Assert.Equal(tied, seen.Skip(1).Take(2));
    }

    [Theory]
    [InlineData("?status=all")]
    [InlineData("?status=")]
    [InlineData("?cursor=not-a-cursor")]
    [InlineData("?limit=0")]
    public async Task A_bad_status_cursor_or_limit_is_400(string query)
    {
        await using var h = Adm.Host(new RecordingProvider());
        var r = await Adm.List(h, query);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("validation_failed", (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Anonymous_is_401_and_a_non_admin_is_403_and_sees_no_text()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (_, client) = await Adm.Seed(h);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Adm.List(h, as_: null)).StatusCode);
        var r = await Adm.List(h, as_: client);
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.DoesNotContain("demo-dispute-text", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Listing_logs_neither_the_text_nor_the_note()
    {
        await using var h = Adm.Host(new RecordingProvider());
        var (id, _) = await Adm.Seed(h);
        await Adm.Seed(h);
        await Adm.Resolve(h, id, Adm.Body("release"));
        Assert.Contains(Adm.Text, await (await Adm.List(h)).Content.ReadAsStringAsync());
        Assert.Contains(Adm.Note, await (await Adm.List(h, "?status=resolved")).Content.ReadAsStringAsync());
        foreach (var line in Adm.LogLines(h))
        {
            Assert.DoesNotContain("demo-dispute-text", line);
            Assert.DoesNotContain("demo-resolution-note", line);
        }
    }
}
