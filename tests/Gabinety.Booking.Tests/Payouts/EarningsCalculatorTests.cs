using Gabinety.Booking.Features.Payouts;
using BookingStatus = Gabinety.Billing.BookingStatus;

namespace Gabinety.Booking.Tests;

/// <summary>The earnings arithmetic and the period rules (CONTRACTS 4.2), with no host and no database: a fixed instant stands in for the clock.</summary>
public class EarningsCalculatorTests
{
    static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    static EarningsBookingRow Row(Guid id, BookingStatus status, long price = 10000, long? payoutMinor = null, PayoutStatus? payout = null, long? payoutAmount = null) =>
        new(id, status, price, payoutMinor, payout, payoutAmount);

    static EarningsTotals Sum(SentPayoutRow[]? sent = null, EarningsBookingRow[]? bookings = null, FeeRow[]? fees = null) =>
        EarningsCalculator.Compute(sent ?? [], bookings ?? [], fees ?? []);

    // ---- the five figures ----

    [Fact]
    public void One_of_each_kind_gives_the_five_figures_summed_by_hand()
    {
        var t = Sum(
            sent: [new(A, 21250)],
            bookings: [Row(B, BookingStatus.Completed, 5000, 5000, PayoutStatus.Pending, 5000), Row(C, BookingStatus.Disputed, 12000)],
            fees: [new(A, 3750)]);
        Assert.Equal(new EarningsTotals(PaidOut: 21250, Pending: 5000, Disputed: 12000, Fees: 3750, Count: 3), t);
    }

    [Fact]
    public void Nothing_in_gives_zeros()
    {
        Assert.Equal(new EarningsTotals(0, 0, 0, 0, 0), Sum());
    }

    [Theory]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.ClientNoShow)]
    [InlineData(BookingStatus.LateCancelledByClient)]
    public void A_booking_owed_a_payout_that_has_no_row_yet_is_pending_with_its_stored_payout(BookingStatus status)
    {
        var t = Sum(bookings: [Row(A, status, price: 25000, payoutMinor: 21250)]);
        Assert.Equal(new EarningsTotals(0, 21250, 0, 0, 1), t);
    }

    [Theory]
    [InlineData(BookingStatus.CancelledByClient)]       // full refund: the practitioner receives nothing (CONTRACTS 9.2)
    [InlineData(BookingStatus.CancelledByPractitioner)]
    [InlineData(BookingStatus.Expired)]
    [InlineData(BookingStatus.PendingPayment)]
    [InlineData(BookingStatus.Confirmed)]
    public void A_refunded_or_unfinished_booking_is_in_no_figure(BookingStatus status)
    {
        Assert.Equal(new EarningsTotals(0, 0, 0, 0, 0), Sum(bookings: [Row(A, status, price: 25000, payoutMinor: 21250)]));
    }

    [Fact]
    public void A_pending_payout_row_uses_its_own_amount_not_the_booking_s_and_a_resolved_booking_counts_through_it()
    {
        // Resolved with `release`: the row exists, the status is not on the spec's list, the row still counts.
        var t = Sum(bookings: [Row(A, BookingStatus.Resolved, price: 25000, payoutMinor: 21250, PayoutStatus.Pending, 4000)]);
        Assert.Equal(new EarningsTotals(0, 4000, 0, 0, 1), t);
    }

    [Fact]
    public void A_failed_payout_is_in_no_figure_and_a_sent_one_is_not_pending_twice()
    {
        var t = Sum(
            sent: [new(B, 7000)],
            bookings:
            [
                Row(A, BookingStatus.Completed, 10000, 8500, PayoutStatus.Failed, 8500),
                Row(B, BookingStatus.Completed, 8000, 7000, PayoutStatus.Sent, 7000),
            ]);
        Assert.Equal(new EarningsTotals(7000, 0, 0, 0, 1), t);
    }

    [Fact]
    public void A_booking_both_sent_and_listed_is_counted_once()
    {
        var t = Sum(sent: [new(A, 100)], bookings: [Row(A, BookingStatus.Disputed, 500)]);
        Assert.Equal(1, t.Count);
    }

    [Fact]
    public void A_dispute_with_a_payout_row_is_not_double_counted_as_disputed()
    {
        // A payout row means the money is already on its way: not "in dispute" any more.
        var t = Sum(bookings: [Row(A, BookingStatus.Disputed, 9000, null, PayoutStatus.Pending, 9000)]);
        Assert.Equal(new EarningsTotals(0, 9000, 0, 0, 1), t);
    }

    [Fact]
    public void Sums_are_long_and_do_not_pass_through_int_or_floating_point()
    {
        const long big = 3_000_000_000; // 30 million zloty in grosz: over int.MaxValue
        var t = Sum(sent: [new(A, big), new(B, big)], fees: [new(A, big), new(B, 1)]);
        Assert.Equal(6_000_000_000, t.PaidOut);
        Assert.Equal(3_000_000_001, t.Fees);
    }

    [Fact]
    public void Odd_grosz_amounts_add_exactly()
    {
        var t = Sum(sent: [new(A, 28333), new(B, 1)], fees: [new(A, 5000), new(B, 0)]);
        Assert.Equal(28334, t.PaidOut);
        Assert.Equal(5000, t.Fees);
    }

    [Fact]
    public void An_overflow_throws_instead_of_wrapping()
    {
        Assert.Throws<OverflowException>(() => Sum(sent: [new(A, long.MaxValue), new(B, 1)]));
    }

    // ---- the period ----

    static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    static EarningsPeriod Resolve(string? from, string? to, DateTimeOffset? now = null)
    {
        Assert.True(EarningsPeriod.TryResolve(from, to, now ?? Now, out var p, out var errors), string.Join(",", errors.Keys));
        return p;
    }

    static string[] Errors(string? from, string? to)
    {
        Assert.False(EarningsPeriod.TryResolve(from, to, Now, out _, out var errors));
        return [.. errors.Keys];
    }

    [Fact]
    public void Without_dates_the_period_is_the_current_month()
    {
        Assert.Equal(new EarningsPeriod(new(2026, 11, 1), new(2026, 11, 30)), Resolve(null, null));
    }

    [Fact]
    public void The_current_month_is_a_Warsaw_month_not_a_UTC_one()
    {
        // 23:30 UTC on 30 November is 00:30 on 1 December in Warsaw.
        var p = Resolve(null, null, new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.Zero));
        Assert.Equal(new EarningsPeriod(new(2026, 12, 1), new(2026, 12, 31)), p);
    }

    [Fact]
    public void A_leap_February_ends_on_the_29th()
    {
        Assert.Equal(new DateOnly(2028, 2, 29), Resolve("2028-02-10", null).To);
    }

    [Fact]
    public void A_missing_bound_follows_the_one_given()
    {
        Assert.Equal(new EarningsPeriod(new(2026, 10, 15), new(2026, 10, 31)), Resolve("2026-10-15", null));
        Assert.Equal(new EarningsPeriod(new(2026, 9, 1), new(2026, 9, 12)), Resolve(null, "2026-09-12"));
    }

    [Fact]
    public void A_single_day_period_is_allowed()
    {
        var p = Resolve("2026-11-16", "2026-11-16");
        Assert.Equal(new EarningsPeriod(new(2026, 11, 16), new(2026, 11, 16)), p);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 23, 0, 0, TimeSpan.Zero), p.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 23, 0, 0, TimeSpan.Zero), p.EndUtcExclusive);
    }

    [Fact]
    public void The_span_limit_is_366_days_between_the_dates()
    {
        Resolve("2026-01-01", "2027-01-02"); // 366 days apart
        Assert.Equal(["to"], Errors("2026-01-01", "2027-01-03")); // 367
    }

    [Theory]
    [InlineData("2026-11-10", "2026-11-09", "to")]
    [InlineData("2026-13-01", null, "from")]
    [InlineData("26-11-01", null, "from")]
    [InlineData(null, "2026-11-31", "to")]
    [InlineData("1999-12-31", "2000-01-02", "from")]
    [InlineData("2099-12-31", "2101-01-01", "from")]
    public void A_bad_period_names_the_field(string? from, string? to, string field)
    {
        Assert.Equal([field], Errors(from, to));
    }

    [Fact]
    public void The_autumn_25_hour_day_is_whole()
    {
        // 25 October 2026: clocks go back at 03:00 in Warsaw, so the day runs from 22:00Z to 23:00Z the next day.
        var p = Resolve("2026-10-25", "2026-10-25");
        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), p.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.Zero), p.EndUtcExclusive);
    }

    [Fact]
    public void The_spring_23_hour_day_is_whole()
    {
        var p = Resolve("2026-03-29", "2026-03-29");
        Assert.Equal(new DateTimeOffset(2026, 3, 28, 23, 0, 0, TimeSpan.Zero), p.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 22, 0, 0, TimeSpan.Zero), p.EndUtcExclusive);
    }

    [Fact]
    public void Adjacent_periods_share_a_boundary_instant_so_no_instant_is_in_two()
    {
        Assert.Equal(Resolve("2026-10-01", "2026-10-31").EndUtcExclusive, Resolve("2026-11-01", "2026-11-30").StartUtc);
    }
}
