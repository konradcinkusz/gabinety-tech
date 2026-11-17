using System.Net;
using System.Net.Http.Json;
using Gabinety.Booking.Features.Payments;
using Gabinety.Billing;
using Gabinety.Contracts.Booking;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>POST /api/v1/bookings/{id}/report-client-no-show (CONTRACTS 4.2, 8.1): from start + 15 min until start + 72 h, practitioner only.</summary>
public class NoShowApiTests
{
    static readonly TimeSpan H = TimeSpan.FromHours(1);

    static Task<HttpResponseMessage> Report(CreateHost h, Guid id, string? as_) =>
        h.ClientFor(as_).PostAsJsonAsync($"/api/v1/bookings/{id}/report-client-no-show", new { });

    static async Task<string> Code(HttpResponseMessage r) => (await r.Body()).GetProperty("code").GetString()!;

    [Theory]
    [InlineData(-(15 * 60) - 1, true)]      // started 15 min 1 s ago
    [InlineData(-(15 * 60), true)]          // exactly 15 min
    [InlineData(-(15 * 60) + 1, false)]     // 14 min 59 s
    [InlineData(0, false)]
    [InlineData(60, false)]
    [InlineData(-(72 * 3600) + 1, true)]    // 72 h is 1 s ahead
    [InlineData(-(72 * 3600), false)]       // exactly 72 h
    [InlineData(-(72 * 3600) - 1, false)]
    public async Task The_window_edges_are_exact(int startInSeconds, bool allowed)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, TimeSpan.FromSeconds(startInSeconds));
        var r = await Report(h, id, Life.Practitioner);
        var row = await Life.Row(h, id);
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal(BookingStatus.ClientNoShow, row.Status);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("invalid_transition", await Code(r));
            Assert.Equal(BookingStatus.Confirmed, row.Status);
            Assert.Null(row.PayoutDueAt);
        }
    }

    [Fact]
    public async Task The_report_sets_payout_due_at_to_the_report_time_and_returns_the_view()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var r = await Report(h, id, Life.Practitioner);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var view = await r.Body();
        Assert.Equal(id, view.GetProperty("id").GetGuid());
        Assert.Equal("ClientNoShow", view.GetProperty("status").GetString());
        Assert.False(view.GetProperty("canReview").GetBoolean()); // no review for a no-show

        var row = await Life.Row(h, id);
        Assert.Equal(h.Clock.GetUtcNow(), row.PayoutDueAt);
        Assert.Null(row.DisputeOpenedAt);
        Assert.Empty(await Life.RefundRows(h, id)); // it counts as completed for money: nothing goes back
        Assert.Equal(PaymentStatus.Succeeded, (await Life.PaymentOf(h, id)).Status);
    }

    [Fact]
    public async Task A_second_report_is_409_and_keeps_the_first_time()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        Assert.Equal(HttpStatusCode.OK, (await Report(h, id, Life.Practitioner)).StatusCode);
        var first = (await Life.Row(h, id)).PayoutDueAt;
        h.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.Conflict, (await Report(h, id, Life.Practitioner)).StatusCode);
        Assert.Equal(first, (await Life.Row(h, id)).PayoutDueAt);
    }

    public static TheoryData<BookingStatus> Illegal => new(Enum.GetValues<BookingStatus>().Where(s => s != BookingStatus.Confirmed));

    [Theory]
    [MemberData(nameof(Illegal))]
    public async Task From_any_status_but_Confirmed_it_is_409_invalid_transition(BookingStatus status)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, status, -2 * H);
        var r = await Report(h, id, Life.Practitioner);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("invalid_transition", await Code(r));
        var row = await Life.Row(h, id);
        Assert.Equal(status, row.Status);
        Assert.Null(row.PayoutDueAt);
    }

    [Theory]
    [InlineData(Life.Client)]
    [InlineData("demo-stranger")]
    public async Task Only_the_practitioner_of_the_booking_may_report_anyone_else_gets_404(string caller)
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        var r = await Report(h, id, caller);
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("not_found", await Code(r));
        Assert.Equal(BookingStatus.Confirmed, (await Life.Row(h, id)).Status);
    }

    [Fact]
    public async Task An_unknown_booking_is_404_and_no_token_is_401()
    {
        await using var h = Life.Host(new RecordingProvider());
        var id = await Life.Seed(h, BookingStatus.Confirmed, -2 * H);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(h, Guid.NewGuid(), Life.Practitioner)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(h, id, null)).StatusCode);
    }
}
