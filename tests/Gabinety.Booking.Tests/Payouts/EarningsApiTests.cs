using System.Net;
using Gabinety.Booking.Features.Payouts;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>GET /api/v1/me/earnings on the in-memory host (CONTRACTS 4.2, T-11.15): who may ask, whose rows count, and which rows fall in which period.</summary>
public class EarningsApiTests : IDisposable
{
    readonly Guid other = Guid.NewGuid();
    readonly CreateHost h;

    public EarningsApiTests() => h = Earn.Host(other);

    HttpClient Me => h.ClientFor(h.OwnerUserId);

    public void Dispose() => h.Dispose();

    [Fact]
    public async Task Without_a_token_the_route_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.ClientFor(null).GetAsync("/api/v1/me/earnings")).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_who_is_not_a_practitioner_is_403_before_the_dates_are_read()
    {
        var r = await h.ClientFor(Earn.NobodyUser).GetAsync("/api/v1/me/earnings?from=not-a-date");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("forbidden", (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Directory_being_down_is_503_not_a_403_that_would_look_like_not_a_practitioner()
    {
        h.Directory.Down = true;
        var r = await Me.GetAsync("/api/v1/me/earnings");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode);
        Assert.Equal("dependency_unavailable", (await r.Body()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task One_sent_one_pending_one_disputed_and_one_fee_give_the_five_figures_summed_by_hand()
    {
        await Earn.SeedScenario(h, h.Practitioner);

        var b = await Earn.Get(Me);

        Assert.Equal((21250L, 5000L, 12000L, 3750L, 3), Earn.Figures(b));
        Assert.Equal("2026-11-01", b.GetProperty("from").GetString());
        Assert.Equal("2026-11-30", b.GetProperty("to").GetString());
    }

    [Fact]
    public async Task A_range_that_excludes_every_row_returns_zeros_with_the_range_echoed()
    {
        await Earn.SeedScenario(h, h.Practitioner);

        var b = await Earn.Get(Me, "?from=2026-01-01&to=2026-01-31");

        Assert.Equal((0L, 0L, 0L, 0L, 0), Earn.Figures(b));
        Assert.Equal("2026-01-01", b.GetProperty("from").GetString());
        Assert.Equal("2026-01-31", b.GetProperty("to").GetString());
    }

    [Fact]
    public async Task Another_practitioners_rows_never_count_and_each_sees_only_their_own()
    {
        await Earn.SeedScenario(h, h.Practitioner);
        // The other practitioner: different amounts, same month, every kind of row.
        var sent = await Earn.Booking(h, other, BookingStatus.Completed, 100000, "2026-11-04T10:00:00Z", payoutMinor: 85000);
        await Earn.Payout(h, sent, other, PayoutStatus.Sent, 85000, "2026-11-07T09:00:00Z");
        await Earn.Fee(h, sent, 15000, "2026-11-06T10:00:00Z");
        var pending = await Earn.Booking(h, other, BookingStatus.Completed, 40000, "2026-11-13T10:00:00Z", payoutMinor: 34000);
        await Earn.Payout(h, pending, other, PayoutStatus.Pending, 34000);
        await Earn.Booking(h, other, BookingStatus.Disputed, 70000, "2026-11-15T10:00:00Z");

        Assert.Equal((21250L, 5000L, 12000L, 3750L, 3), Earn.Figures(await Earn.Get(Me)));
        Assert.Equal((85000L, 34000L, 70000L, 15000L, 3), Earn.Figures(await Earn.Get(h.ClientFor(Earn.OtherUser))));
    }

    [Fact]
    public async Task The_caller_cannot_ask_for_somebody_elses_data_through_a_parameter()
    {
        await Earn.Booking(h, other, BookingStatus.Disputed, 70000, "2026-11-15T10:00:00Z");

        var b = await Earn.Get(Me, $"?practitionerId={other}&practitioner_id={other}");

        Assert.Equal((0L, 0L, 0L, 0L, 0), Earn.Figures(b));
    }

    [Fact]
    public async Task Cancellations_refunds_and_failed_payouts_are_in_no_figure_and_unpaid_completions_are_pending()
    {
        var p = h.Practitioner;
        // Money back to the client: nothing for the practitioner, whatever stray payout amount the row holds.
        await Earn.Booking(h, p, BookingStatus.CancelledByClient, 25000, "2026-11-10T10:00:00Z");
        await Earn.Booking(h, p, BookingStatus.CancelledByPractitioner, 25000, "2026-11-11T10:00:00Z");
        await Earn.Booking(h, p, BookingStatus.Expired, 25000, "2026-11-12T10:00:00Z");
        // A payout that failed is neither paid out nor pending.
        var failed = await Earn.Booking(h, p, BookingStatus.Completed, 10000, "2026-11-09T10:00:00Z", payoutMinor: 8500);
        await Earn.Payout(h, failed, p, PayoutStatus.Failed, 8500);
        // The practitioner keeps the share of a late cancellation and a client no-show; the payout job has not taken them up yet.
        await Earn.Booking(h, p, BookingStatus.LateCancelledByClient, 8000, "2026-11-13T10:00:00Z", payoutMinor: 6800);
        await Earn.Booking(h, p, BookingStatus.ClientNoShow, 5000, "2026-11-14T10:00:00Z", payoutMinor: 4250);

        Assert.Equal((0L, 11050L, 0L, 0L, 2), Earn.Figures(await Earn.Get(Me)));
    }

    [Fact]
    public async Task A_resolved_dispute_released_to_the_practitioner_is_paid_out_by_its_sent_at_wherever_the_booking_started()
    {
        var id = await Earn.Booking(h, h.Practitioner, BookingStatus.Resolved, 20000, "2026-10-20T10:00:00Z", payoutMinor: 17000);
        await Earn.Payout(h, id, h.Practitioner, PayoutStatus.Sent, 17000, "2026-11-08T09:00:00Z");

        Assert.Equal((17000L, 0L, 0L, 0L, 1), Earn.Figures(await Earn.Get(Me)));
        Assert.Equal((0L, 0L, 0L, 0L, 0), Earn.Figures(await Earn.Get(Me, "?from=2026-10-01&to=2026-10-31")));
    }

    [Fact]
    public async Task A_sent_payout_belongs_to_the_period_of_its_sent_at_half_open_in_Warsaw_time()
    {
        // November in Warsaw is [2026-10-31T23:00Z, 2026-11-30T23:00Z). The four amounts tell which payouts were counted: only the two inside, 1 + 100.
        async Task Sent(long amount, string sentAt)
        {
            var id = await Earn.Booking(h, h.Practitioner, BookingStatus.Completed, amount, "2026-11-02T10:00:00Z", payoutMinor: amount);
            await Earn.Payout(h, id, h.Practitioner, PayoutStatus.Sent, amount, sentAt);
        }
        await Sent(1, "2026-10-31T23:00:00Z");           // first instant: in
        await Sent(10, "2026-10-31T22:59:59.9999999Z");  // just before: out
        await Sent(100, "2026-11-30T22:59:59.9999999Z"); // last instant: in
        await Sent(1000, "2026-11-30T23:00:00Z");        // first instant of December: out

        Assert.Equal(101L, Earn.Figures(await Earn.Get(Me)).PaidOut);
    }

    [Fact]
    public async Task A_fee_belongs_to_the_period_of_its_ledger_entry_half_open_in_Warsaw_time()
    {
        var id = await Earn.Booking(h, h.Practitioner, BookingStatus.Completed, 25000, "2026-11-02T10:00:00Z");
        await Earn.Fee(h, id, 1, "2026-10-31T23:00:00Z");
        await Earn.Fee(h, id, 10, "2026-10-31T22:59:59.9999999Z");
        await Earn.Fee(h, id, 100, "2026-11-30T22:59:59.9999999Z");
        await Earn.Fee(h, id, 1000, "2026-11-30T23:00:00Z");

        Assert.Equal(101L, Earn.Figures(await Earn.Get(Me)).Fees);
    }

    [Fact]
    public async Task Pending_and_disputed_belong_to_the_period_of_the_booking_s_start_in_Warsaw_time()
    {
        // 23:30Z on 30 November is 00:30 on 1 December in Warsaw: a December booking, not a November one.
        await Earn.Booking(h, h.Practitioner, BookingStatus.Disputed, 3000, "2026-11-30T23:30:00Z");
        await Earn.Booking(h, h.Practitioner, BookingStatus.Disputed, 200, "2026-11-30T22:30:00Z"); // 23:30 on the 30th: November
        await Earn.Booking(h, h.Practitioner, BookingStatus.Completed, 40, "2026-10-31T22:30:00Z", payoutMinor: 40); // 23:30 on 31 October: October

        Assert.Equal((0L, 0L, 200L, 0L, 1), Earn.Figures(await Earn.Get(Me, "?from=2026-11-01&to=2026-11-30")));
        Assert.Equal((0L, 0L, 3000L, 0L, 1), Earn.Figures(await Earn.Get(Me, "?from=2026-12-01&to=2026-12-31")));
    }

    [Fact]
    public async Task Dates_are_inclusive_so_a_one_day_range_holds_that_whole_day()
    {
        await Earn.Booking(h, h.Practitioner, BookingStatus.Disputed, 700, "2026-11-12T08:00:00Z");  // 09:00 Warsaw
        await Earn.Booking(h, h.Practitioner, BookingStatus.Disputed, 50, "2026-11-12T22:59:00Z");   // 23:59 Warsaw
        await Earn.Booking(h, h.Practitioner, BookingStatus.Disputed, 9, "2026-11-12T23:00:00Z");    // 00:00 on the 13th

        Assert.Equal(750L, Earn.Figures(await Earn.Get(Me, "?from=2026-11-12&to=2026-11-12")).Disputed);
    }

    [Theory]
    [InlineData("?from=2026-13-01", "from")]
    [InlineData("?to=yesterday", "to")]
    [InlineData("?from=2026-11-10&to=2026-11-09", "to")]
    [InlineData("?from=2026-01-01&to=2027-01-03", "to")]
    public async Task A_bad_range_is_400_validation_failed_naming_the_field(string query, string field)
    {
        var r = await Me.GetAsync("/api/v1/me/earnings" + query);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var b = await r.Body();
        Assert.Equal("validation_failed", b.GetProperty("code").GetString());
        Assert.Equal([field], b.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task The_longest_allowed_range_is_366_days_apart_and_answers()
    {
        var b = await Earn.Get(Me, "?from=2026-01-01&to=2027-01-02");

        Assert.Equal((0L, 0L, 0L, 0L, 0), Earn.Figures(b));
        Assert.Equal("2027-01-02", b.GetProperty("to").GetString());
    }
}
