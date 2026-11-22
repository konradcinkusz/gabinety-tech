using System.Net;
using System.Text;
using System.Text.Json;
using Gabinety.Contracts.Booking;
using Gabinety.Booking.Features.Bookings.Create;
using Gabinety.Booking.Features.Payments;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>GET /api/v1/me/bookings (CONTRACTS 4.2, 0) and the flags of GET /api/v1/bookings/{id}. Every test uses its own random user ids: the InMemory database is shared.</summary>
public class MyBookingsApiTests
{
    static readonly TimeSpan H = TimeSpan.FromHours(1);

    sealed record Who(string Client, string Practitioner)
    {
        public static Who New() => new("demo-c-" + Guid.NewGuid().ToString("N"), "demo-p-" + Guid.NewGuid().ToString("N"));
    }

    static async Task<Guid> Seed(CreateHost h, Who w, TimeSpan startIn, BookingStatus status = BookingStatus.Confirmed, Guid? id = null,
        string? clientOverride = null, string? practitionerOverride = null, long? fee = null, long? payout = null)
    {
        id ??= Guid.NewGuid();
        var start = h.Clock.GetUtcNow() + startIn;
        await h.Seed(db =>
        {
            db.Bookings.Add(new BookingEntity
            {
                Id = id.Value, ClientUserId = clientOverride ?? w.Client, PractitionerId = h.Practitioner, PractitionerUserId = practitionerOverride ?? w.Practitioner,
                OfferingId = h.Offering, OfferingTitle = "demo-offering", DurationMinutes = 60, PriceMinor = 10000, StartUtc = start, EndUtc = start + H,
                Status = status, BriefText = "demo-brief: private words of the client", IdempotencyKey = Guid.NewGuid(), CreatedAt = h.Clock.GetUtcNow(),
                MeetingUrlSnapshot = status == BookingStatus.Confirmed ? "https://meet.demo.invalid/secret-room" : null,
                HoldExpiresAt = status == BookingStatus.PendingPayment ? h.Clock.GetUtcNow() + H : null, FeeMinor = fee, PayoutMinor = payout,
            });
            db.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(), BookingId = id.Value, Provider = "Mock", AmountMinor = 10000, Status = PaymentStatus.Succeeded,
                CreatedAt = h.Clock.GetUtcNow(), UpdatedAt = h.Clock.GetUtcNow(),
            });
        });
        return id.Value;
    }

    static CreateHost Host() => Life.Host(new RecordingProvider());

    static async Task<(HttpStatusCode, JsonElement)> List(CreateHost h, string? user, string query)
    {
        var r = await h.ClientFor(user).GetAsync("/api/v1/me/bookings" + query);
        return (r.StatusCode, r.StatusCode == HttpStatusCode.Unauthorized ? default : await r.Body());
    }

    static bool Absent(JsonElement e, string name) => !e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null;

    static List<Guid> Ids(JsonElement body) => body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

    [Fact]
    public async Task It_needs_a_signed_in_user()
    {
        await using var h = Host();
        Assert.Equal(HttpStatusCode.Unauthorized, (await List(h, null, "?role=client")).Item1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?role=")]
    [InlineData("?role=admin")]
    [InlineData("?role=Client ")]
    [InlineData("?role=client&status=Nope")]
    [InlineData("?role=client&cursor=not-a-cursor")]
    [InlineData("?role=client&cursor=e30")]            // base64url of {} : well-formed but not ours
    [InlineData("?role=client&limit=0")]
    [InlineData("?role=client&limit=25")]
    [InlineData("?role=client&limit=abc")]
    public async Task A_bad_role_status_cursor_or_limit_is_400_validation_failed(string query)
    {
        await using var h = Host();
        var (code, body) = await List(h, "demo-anyone", query);
        Assert.Equal(HttpStatusCode.BadRequest, code);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task It_returns_only_the_callers_rows_for_the_chosen_role_and_a_user_with_both_roles_sees_each_set()
    {
        await using var h = Host();
        var w = Who.New();
        var other = Who.New();
        var asClient = await Seed(h, w, TimeSpan.FromDays(2));
        var asPractitioner = await Seed(h, other, TimeSpan.FromDays(3), clientOverride: other.Client, practitionerOverride: w.Client); // w.Client is the practitioner here
        var someoneElses = await Seed(h, other, TimeSpan.FromDays(4));

        var (_, asC) = await List(h, w.Client, "?role=client");
        var (_, asP) = await List(h, w.Client, "?role=practitioner");
        Assert.Equal([asClient], Ids(asC));
        Assert.Equal([asPractitioner], Ids(asP));
        Assert.DoesNotContain(someoneElses, Ids(asC).Concat(Ids(asP)));
        Assert.Empty(Ids((await List(h, "demo-nobody-" + Guid.NewGuid(), "?role=client")).Item2));
    }

    [Fact]
    public async Task It_is_newest_start_first_with_equal_starts_ordered_by_id_descending()
    {
        await using var h = Host();
        var w = Who.New();
        var oldest = await Seed(h, w, TimeSpan.FromDays(1));
        var newest = await Seed(h, w, TimeSpan.FromDays(9));
        var tieLow = await Seed(h, w, TimeSpan.FromDays(5), id: new Guid("00000000-0000-0000-0000-000000000001"));
        var tieHigh = await Seed(h, w, TimeSpan.FromDays(5), id: new Guid("00000000-0000-0000-0000-0000000000ff")); // the clock is fixed: the same start to the tick
        var (_, body) = await List(h, w.Client, "?role=client");
        var ids = Ids(body);
        Assert.Equal(newest, ids[0]);
        Assert.Equal(oldest, ids[^1]);
        var tied = ids.Where(i => i == tieLow || i == tieHigh).ToList();
        Assert.Equal(new[] { tieLow, tieHigh }.OrderByDescending(x => x).ToList(), tied);
    }

    [Fact]
    public async Task Paging_with_the_cursor_yields_every_row_once_and_the_last_page_has_no_next_cursor()
    {
        await using var h = Host();
        var w = Who.New();
        var all = new List<Guid>();
        for (var i = 0; i < 7; i++) all.Add(await Seed(h, w, TimeSpan.FromDays(i % 3 + 1))); // many equal starts across page borders

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        while (true)
        {
            var (code, body) = await List(h, w.Client, "?role=client&limit=3" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.Equal(HttpStatusCode.OK, code);
            seen.AddRange(Ids(body));
            pages++;
            if (!body.TryGetProperty("nextCursor", out var next)) break;
            Assert.Equal(JsonValueKind.String, next.ValueKind);
            cursor = next.GetString();
            Assert.InRange(pages, 1, 5);
        }
        Assert.Equal(3, pages);
        Assert.Equal(all.Count, seen.Count);
        Assert.Equal(all.OrderBy(x => x), seen.OrderBy(x => x));
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task A_page_that_ends_exactly_on_the_last_row_has_no_next_cursor()
    {
        await using var h = Host();
        var w = Who.New();
        for (var i = 0; i < 3; i++) await Seed(h, w, TimeSpan.FromDays(i + 1));
        var (_, body) = await List(h, w.Client, "?role=client&limit=3");
        Assert.Equal(3, Ids(body).Count);
        Assert.False(body.TryGetProperty("nextCursor", out _));
    }

    [Fact]
    public async Task A_cursor_from_another_role_or_status_or_a_tampered_one_is_400()
    {
        await using var h = Host();
        var w = Who.New();
        for (var i = 0; i < 3; i++) await Seed(h, w, TimeSpan.FromDays(i + 1));
        var (_, page) = await List(h, w.Client, "?role=client&limit=1");
        var cursor = page.GetProperty("nextCursor").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await List(h, w.Client, $"?role=client&limit=1&cursor={cursor}")).Item1);
        Assert.Equal(HttpStatusCode.BadRequest, (await List(h, w.Client, $"?role=practitioner&cursor={cursor}")).Item1);
        Assert.Equal(HttpStatusCode.BadRequest, (await List(h, w.Client, $"?role=client&status=Confirmed&cursor={cursor}")).Item1);
        var flipped = cursor[..^2] + (cursor[^2] == 'A' ? "B" : "A") + cursor[^1];
        Assert.Equal(HttpStatusCode.BadRequest, (await List(h, w.Client, $"?role=client&cursor={flipped}")).Item1);
        Assert.Equal(HttpStatusCode.BadRequest, (await List(h, w.Client, $"?role=client&cursor={cursor}x")).Item1);
        var junk = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"x\":1}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(HttpStatusCode.BadRequest, (await List(h, w.Client, $"?role=client&cursor={junk}")).Item1);
    }

    [Fact]
    public async Task The_status_filter_keeps_only_that_status()
    {
        await using var h = Host();
        var w = Who.New();
        var confirmed = await Seed(h, w, TimeSpan.FromDays(1));
        await Seed(h, w, TimeSpan.FromDays(2), BookingStatus.PendingPayment);
        await Seed(h, w, TimeSpan.FromDays(3), BookingStatus.CancelledByClient);
        var (_, body) = await List(h, w.Client, "?role=client&status=Confirmed");
        Assert.Equal([confirmed], Ids(body));
        Assert.Equal(3, Ids((await List(h, w.Client, "?role=client")).Item2).Count);
    }

    [Fact]
    public async Task No_brief_meeting_url_or_fee_reaches_the_wrong_party_in_the_list()
    {
        await using var h = Host();
        var w = Who.New();
        var pending = await Seed(h, w, TimeSpan.FromDays(1), BookingStatus.PendingPayment, fee: 1500, payout: 8500);
        var confirmed = await Seed(h, w, TimeSpan.FromDays(2), fee: 1500, payout: 8500);

        var (_, client) = await List(h, w.Client, "?role=client");
        var raw = client.GetRawText();
        Assert.Contains("demo-brief", raw);                      // the client sees the brief always
        Assert.Contains("secret-room", raw);                     // and the link of the Confirmed one
        foreach (var i in client.GetProperty("items").EnumerateArray())
        {
            Assert.True(Absent(i, "fee"));
            Assert.True(Absent(i, "payout"));
        }

        var (_, prac) = await List(h, w.Practitioner, "?role=practitioner");
        var byId = prac.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("id").GetGuid());
        Assert.False(byId[pending].TryGetProperty("brief", out var pb) && pb.ValueKind == JsonValueKind.Object);
        Assert.True(Absent(byId[pending], "meetingUrl"));
        Assert.Equal(JsonValueKind.Object, byId[confirmed].GetProperty("brief").ValueKind);
        Assert.Equal("https://meet.demo.invalid/secret-room", byId[confirmed].GetProperty("meetingUrl").GetString());
        Assert.Equal(1500, byId[confirmed].GetProperty("fee").GetProperty("amountMinor").GetInt64());
        Assert.Equal(8500, byId[confirmed].GetProperty("payout").GetProperty("amountMinor").GetInt64());

        // a client listing as the practitioner role of someone else gets nothing of it
        Assert.DoesNotContain("demo-brief", (await List(h, w.Client, "?role=practitioner")).Item2.GetRawText());
    }

    [Fact]
    public async Task The_single_view_and_the_list_agree_for_each_party_and_the_flags_follow_the_clock()
    {
        await using var h = Host();
        var w = Who.New();
        var id = await Seed(h, w, TimeSpan.FromMinutes(20), fee: 1500, payout: 8500); // starts in 20 min
        h.Clock.Advance(TimeSpan.FromMinutes(40));                                    // 20 min after the start: the no-show claim is open

        foreach (var (user, role) in new[] { (w.Client, "client"), (w.Practitioner, "practitioner") })
        {
            var single = await (await h.ClientFor(user).GetAsync($"/api/v1/bookings/{id}")).Body();
            var (_, list) = await List(h, user, $"?role={role}");
            Assert.Equal(single.GetRawText(), list.GetProperty("items")[0].GetRawText());
        }
        var c = await (await h.ClientFor(w.Client).GetAsync($"/api/v1/bookings/{id}")).Body();
        Assert.True(c.GetProperty("canCancel").GetBoolean());
        Assert.True(c.GetProperty("canDispute").GetBoolean());
        Assert.False(c.GetProperty("canReportNoShow").GetBoolean());
        Assert.False(c.GetProperty("canReview").GetBoolean());
        var p = await (await h.ClientFor(w.Practitioner).GetAsync($"/api/v1/bookings/{id}")).Body();
        Assert.True(p.GetProperty("canCancel").GetBoolean());
        Assert.False(p.GetProperty("canDispute").GetBoolean());
        Assert.True(p.GetProperty("canReportNoShow").GetBoolean());
    }

    [Fact]
    public async Task Directorys_name_and_slug_are_shown_and_a_row_without_a_snapshot_reads_Klient()
    {
        await using var h = Host();
        var w = Who.New();
        var id = await Seed(h, w, TimeSpan.FromDays(1));
        var v = await (await h.ClientFor(w.Practitioner).GetAsync($"/api/v1/bookings/{id}")).Body();
        Assert.Equal("Demo Practitioner", v.GetProperty("practitioner").GetProperty("displayName").GetString());
        Assert.Equal("demo-practitioner", v.GetProperty("practitioner").GetProperty("slug").GetString());
        Assert.Equal("Klient", v.GetProperty("client").GetProperty("displayName").GetString()); // no snapshot on the seeded row
    }
}
