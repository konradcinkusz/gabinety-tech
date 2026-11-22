using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gabinety.Booking.Clients;
using Gabinety.Booking.Features.Bookings.View;
using Gabinety.Booking.Features.Internal.Erasure;
using Gabinety.Booking.Features.Lifecycle.Cancel;
using Gabinety.Booking.Features.Payments;
using Gabinety.Contracts.Booking;
using BookingEntity = Gabinety.Booking.Features.Bookings.Entities.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>F-03: the counter-party's display name on booking reads. F-11: <c>cancelOutcome</c> before the user confirms. F-13: the dispute window as the seeded state meets it.</summary>
public class PartyNamesTests
{
    static readonly TimeSpan H = TimeSpan.FromHours(1), H24 = TimeSpan.FromHours(24);

    static async Task<JsonElement> Get(HttpClient c, string path)
    {
        var r = await c.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Theory]
    [InlineData("demo-client@example.test", "Klient D.")]
    [InlineData("anna.kowalska@example.test", "Klient A.")]
    [InlineData("zażółć", "Klient Z.")]
    [InlineData("  ...-_", "Klient")]
    [InlineData("", "Klient")]
    [InlineData(null, "Klient")]
    public void The_client_snapshot_is_Klient_and_one_initial_and_never_the_address(string? claim, string expected)
    {
        var snapshot = PartyNamesLookup.ClientSnapshot(claim);
        Assert.Equal(expected, snapshot);
        Assert.DoesNotContain("@", snapshot);
        Assert.True(snapshot.Length <= 9);
    }

    [Fact]
    public async Task Detail_and_list_carry_the_practitioner_name_for_the_client_and_the_client_name_for_the_practitioner()
    {
        await using var h = Life.Host(new RecordingProvider());
        h.NameClaim = "demo-client-1@example.test";
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24 * 3);
        // unique parties: the in-memory database is shared by the hosts of one test process and the list shows 12 rows a page
        var client = "demo-client-" + Guid.NewGuid().ToString("n");
        var practitioner = "demo-practitioner-" + Guid.NewGuid().ToString("n");
        await h.Seed(db => { var b = db.Bookings.Single(x => x.Id == id); b.ClientDisplayName = "Klient D."; b.ClientUserId = client; b.PractitionerUserId = practitioner; });

        var asClient = await Get(h.ClientFor(client), $"/api/v1/bookings/{id}");
        Assert.Equal("Demo Practitioner", asClient.GetProperty("practitioner").GetProperty("displayName").GetString());
        Assert.Equal("demo-practitioner", asClient.GetProperty("practitioner").GetProperty("slug").GetString());
        Assert.Equal(JsonValueKind.Null, asClient.GetProperty("client").ValueKind); // a client is not shown to themself

        var asPractitioner = await Get(h.ClientFor(practitioner), $"/api/v1/bookings/{id}");
        Assert.Equal("Klient D.", asPractitioner.GetProperty("client").GetProperty("displayName").GetString());
        Assert.DoesNotContain("example.test", asPractitioner.GetRawText());

        var list = await Get(h.ClientFor(client), "/api/v1/me/bookings?role=client");
        // the in-memory database is shared by the hosts of one test process: find the row by id
        JsonElement Mine(JsonElement page) => page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("Demo Practitioner", Mine(list).GetProperty("practitioner").GetProperty("displayName").GetString());
        var plist = await Get(h.ClientFor(practitioner), "/api/v1/me/bookings?role=practitioner");
        Assert.Equal("Klient D.", Mine(plist).GetProperty("client").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_booking_made_with_the_token_stores_Klient_and_an_initial_not_the_address()
    {
        await using var h = Life.Host(new RecordingProvider());
        h.NameClaim = "demo-client-1@example.test";
        await h.WorkWeek();
        var created = await CreateHost.Post(h.ClientFor(Life.Client), h.Req(), Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = Assert.Single(await h.Bookings());
        Assert.Equal("Klient D.", row.ClientDisplayName);
    }

    [Fact]
    public async Task A_row_without_a_snapshot_reads_Klient()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24 * 3);
        var v = await Get(h.ClientFor(Life.Practitioner), $"/api/v1/bookings/{id}");
        Assert.Equal("Klient", v.GetProperty("client").GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Erased_users_read_Usuniety_uzytkownik_on_both_sides()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24 * 3);
        var clientToken = ErasureFeature.Token(Life.Client);
        var practitionerToken = ErasureFeature.Token(Life.Practitioner);
        await h.Seed(db => { var b = db.Bookings.Single(x => x.Id == id); b.ClientUserId = clientToken; b.ClientDisplayName = null; b.PractitionerUserId = practitionerToken; });

        var asClient = await Get(h.ClientFor(clientToken), $"/api/v1/bookings/{id}");
        Assert.Equal("Usunięty użytkownik", asClient.GetProperty("practitioner").GetProperty("displayName").GetString());
        var asPractitioner = await Get(h.ClientFor(practitionerToken), $"/api/v1/bookings/{id}");
        Assert.Equal("Usunięty użytkownik", asPractitioner.GetProperty("client").GetProperty("displayName").GetString());
    }

    [Fact]
    public void Erasing_the_client_clears_the_snapshot()
    {
        var b = new BookingEntity { ClientUserId = "demo-u", ClientDisplayName = "Klient D.", PractitionerUserId = "demo-p" };
        var method = typeof(ErasureFeature).GetMethod("Anonymise", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        method.Invoke(null, [b, "demo-u", ErasureFeature.Token("demo-u")]);
        Assert.Null(b.ClientDisplayName);
    }

    [Fact]
    public async Task When_Directory_is_down_the_view_still_renders_with_a_generic_practitioner_label()
    {
        await using var h = Life.Host(new RecordingProvider());
        h.DirectoryDown = true;
        var id = await Life.Seed(h, BookingStatus.Confirmed, H24 * 3);
        var v = await Get(h.ClientFor(Life.Client), $"/api/v1/bookings/{id}");
        Assert.Equal("Programista", v.GetProperty("practitioner").GetProperty("displayName").GetString());
    }
}

/// <summary>F-11: <c>cancelOutcome</c> is computed by the code that performs the cancel, and the two agree on both sides of the 24 h boundary.</summary>
public class CancelOutcomeTests
{
    static readonly TimeSpan H24 = TimeSpan.FromHours(24), S = TimeSpan.FromSeconds(1);

    [Theory]
    [InlineData(24 * 3600, "full_refund", 10000, "Refunded")]       // exactly 24 h: the full refund
    [InlineData(24 * 3600 - 1, "no_refund", 0, "Succeeded")]        // 1 s inside: no refund
    [InlineData(30 * 24 * 3600, "full_refund", 10000, "Refunded")]
    [InlineData(60, "no_refund", 0, "Succeeded")]
    public async Task The_outcome_shown_before_the_client_cancels_is_the_outcome_of_the_cancel(int startInSeconds, string outcome, long refund, string paymentStatus)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromSeconds(startInSeconds));

        var view = JsonDocument.Parse(await h.ClientFor(Life.Client).GetStringAsync($"/api/v1/bookings/{id}")).RootElement;
        Assert.True(view.GetProperty("canCancel").GetBoolean());
        Assert.Equal(outcome, view.GetProperty("cancelOutcome").GetProperty("outcome").GetString());
        Assert.Equal(refund, view.GetProperty("cancelOutcome").GetProperty("refund").GetProperty("amountMinor").GetInt64());

        var cancelled = JsonDocument.Parse(await (await Life.Cancel(h, id, Life.Client)).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(paymentStatus, cancelled.GetProperty("payment").GetProperty("status").GetString());
        Assert.Equal(refund, cancelled.GetProperty("payment").GetProperty("refunded").GetProperty("amountMinor").GetInt64());
        Assert.Equal(JsonValueKind.Null, cancelled.GetProperty("cancelOutcome").ValueKind); // nothing left to cancel
    }

    [Fact]
    public async Task The_practitioner_is_always_told_full_refund_and_a_booking_that_cannot_be_cancelled_has_no_outcome()
    {
        await using var h = Life.Host(new RecordingProvider());
        var soon = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromMinutes(30));
        var v = JsonDocument.Parse(await h.ClientFor(Life.Practitioner).GetStringAsync($"/api/v1/bookings/{soon}")).RootElement;
        Assert.Equal("full_refund", v.GetProperty("cancelOutcome").GetProperty("outcome").GetString());
        Assert.Equal(10000, v.GetProperty("cancelOutcome").GetProperty("refund").GetProperty("amountMinor").GetInt64());

        var done = await Life.Seed(h, BookingStatus.Completed, -H24);
        var d = JsonDocument.Parse(await h.ClientFor(Life.Client).GetStringAsync($"/api/v1/bookings/{done}")).RootElement;
        Assert.False(d.GetProperty("canCancel").GetBoolean());
        Assert.Equal(JsonValueKind.Null, d.GetProperty("cancelOutcome").ValueKind);
    }

    [Fact]
    public void The_planner_is_the_single_source_at_the_boundary_and_names_partial_only_for_a_part_refund()
    {
        var start = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
        var b = new BookingEntity { Status = BookingStatus.Confirmed, StartUtc = start, EndUtc = start.AddHours(1), PriceMinor = 18000 };
        Assert.Equal((CancelOutcomeKind.FullRefund, 18000L), Tuple(CancelPlanner.Plan(b, byClient: true, start - H24)));
        Assert.Equal((CancelOutcomeKind.NoRefund, 0L), Tuple(CancelPlanner.Plan(b, byClient: true, start - H24 + S)));
        Assert.Equal((CancelOutcomeKind.FullRefund, 18000L), Tuple(CancelPlanner.Plan(b, byClient: false, start + S)));
        Assert.Null(CancelPlanner.Plan(b, byClient: false, b.EndUtc));               // the practitioner's time rule
        b.Status = BookingStatus.Completed;
        Assert.Null(CancelPlanner.Plan(b, byClient: true, start - H24));             // the state table
    }

    static (CancelOutcomeKind, long) Tuple(CancelPlan? p) => (p!.Value.Outcome, p.Value.Refund);
}

/// <summary>F-13: a Completed booking is disputable by the client exactly until start + 72 h, whether the start lies ahead (the seeded state) or behind.</summary>
public class DisputeWindowTests
{
    static readonly DateTimeOffset Start = new(2026, 12, 1, 10, 0, 0, TimeSpan.Zero);
    sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    static readonly TimeSpan H = TimeSpan.FromHours(1), S = TimeSpan.FromSeconds(1);

    [Theory]
    [InlineData(-48 * 3600, true)]      // the seeded state: the clock was moved forward to complete it, then back; the start lies ahead
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(48 * 3600, true)]
    [InlineData(72 * 3600 - 1, true)]   // the last second
    [InlineData(72 * 3600, false)]      // start + 72 h exactly: over
    [InlineData(72 * 3600 + 1, false)]
    public void canDispute_is_true_for_a_Completed_booking_exactly_inside_the_window(int offsetSeconds, bool expected)
    {
        var b = new BookingEntity { ClientUserId = "demo-client", PractitionerUserId = "demo-practitioner", Status = BookingStatus.Completed, StartUtc = Start, EndUtc = Start + H, EndedAt = Start + H };
        var now = Start + TimeSpan.FromSeconds(offsetSeconds);
        Assert.Equal(expected, BookingPermissions.For(b, "demo-client", new Clock(now)).CanDispute);
        Assert.False(BookingPermissions.For(b, "demo-practitioner", new Clock(now)).CanDispute);
    }
}
